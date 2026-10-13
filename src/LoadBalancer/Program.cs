using System.Diagnostics;
using System.Threading.RateLimiting;
using LoadBalancer;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Ho tro reload cau hinh dong thoi runtime qua LbState
var signalSource = new SignalConfigurationSource();
((IConfigurationBuilder)builder.Configuration).Add(signalSource);
builder.Services.AddSingleton(signalSource.Provider);
builder.Services.AddSingleton<LbState>();
builder.Services.AddSingleton<LbMetrics>();
builder.Services.AddHttpClient();

// Rate Limiting tap trung tai Load Balancer: Toi da 5 requests trong 10 giay
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("demo-limiter", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueLimit = 0;
    });
});

builder.Services.AddSingleton<Yarp.ReverseProxy.Health.IPassiveHealthCheckPolicy, FailOn5xxPassiveHealthPolicy>();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // Chon thuat toan va che do Session Affinity dong qua LbState
    .AddConfigFilter<LoadBalancerModeConfigFilter>()
    // Cho biet node nao da phuc vu request -> rat tien khi test LB.
    .AddTransforms(ctx =>
    {
        ctx.AddResponseTransform(t =>
        {
            var dest = t.HttpContext.Features.Get<IReverseProxyFeature>()?.ProxiedDestination;
            if (dest is not null)
            {
                t.HttpContext.Response.Headers["X-Served-By"] = dest.DestinationId;

                t.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("LB")
                    .LogInformation("{Method} {Path} -> {Node}",
                        t.HttpContext.Request.Method,
                        t.HttpContext.Request.Path,
                        dest.DestinationId);
            }
            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

// Middleware do do tre va ghi nhan metrics (an toan cho ca endpoint noi bo)
app.Use(async (context, next) =>
{
    // Khong ghi nhan traffic noi bo cua chinh Dashboard
    if (context.Request.Path.StartsWithSegments("/dashboard") ||
        context.Request.Path.StartsWithSegments("/lb-dashboard") ||
        context.Request.Path.StartsWithSegments("/api/lb"))
    {
        await next();
        return;
    }

    var sw = Stopwatch.StartNew();
    context.Response.OnStarting(() =>
    {
        sw.Stop();
        var dest = context.Features.Get<IReverseProxyFeature>()?.ProxiedDestination;
        var metrics = context.RequestServices.GetRequiredService<LbMetrics>();
        metrics.RecordRequest(
            dest?.DestinationId,
            context.Request.Method,
            context.Request.Path.Value ?? "/",
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
        return Task.CompletedTask;
    });

    await next();
});

app.UseRouting();
app.UseRateLimiter();

// Map Dashboard va cac API quan sat / dieu khien
app.MapDashboard();

// Transparent Auto-Failover Middleware:
// Khi nguoi dung refresh hoac gui request GET/HEAD ma node bi loi 503/5xx,
// LB danh dau Unhealthy node do ngay lap tuc va thu lai (Failover) sang node khoe manh con lai.
// Nho do nguoi dung khong bao gio bi hien loi 503 tren trinh duyet!
app.Use(async (context, next) =>
{
    if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
        context.Request.Path.StartsWithSegments("/dashboard") ||
        context.Request.Path.StartsWithSegments("/lb-dashboard") ||
        context.Request.Path.StartsWithSegments("/api/lb"))
    {
        await next();
        return;
    }

    var originalBody = context.Response.Body;
    using var memStream = new MemoryStream();
    context.Response.Body = memStream;

    try
    {
        await next();

        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var proxyStateLookup = context.RequestServices.GetRequiredService<IProxyStateLookup>();
            var healthUpdater = context.RequestServices.GetRequiredService<IDestinationHealthUpdater>();

            if (proxyStateLookup.TryGetCluster("demo", out var clusterState))
            {
                var failedDest = context.Features.Get<IReverseProxyFeature>()?.ProxiedDestination;
                if (failedDest != null)
                {
                    healthUpdater.SetPassive(clusterState, failedDest, DestinationHealth.Unhealthy, TimeSpan.FromSeconds(30));
                }

                // Tim node tiep theo con song
                var healthyDest = clusterState.Destinations.Values
                    .FirstOrDefault(d => d != failedDest && d.Health.Passive != DestinationHealth.Unhealthy);

                if (healthyDest != null)
                {
                    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AutoFailover");
                    logger.LogWarning(">> [TRANSPARENT FAILOVER] Node {Failed} tra ve HTTP {Status}. Tu dong failover sang {Backup}!",
                        failedDest?.DestinationId ?? "unknown", context.Response.StatusCode, healthyDest.DestinationId);

                    memStream.SetLength(0);
                    context.Response.Clear();

                    var clientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                    var client = clientFactory.CreateClient();

                    var targetUrl = $"{healthyDest.Model.Config.Address.TrimEnd('/')}{context.Request.Path}{context.Request.QueryString}";
                    using var backupRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);

                    foreach (var h in context.Request.Headers)
                    {
                        if (!h.Key.StartsWith(':') && !h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                        {
                            backupRequest.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
                        }
                    }

                    using var backupResponse = await client.SendAsync(backupRequest, context.RequestAborted);
                    context.Response.StatusCode = (int)backupResponse.StatusCode;

                    foreach (var h in backupResponse.Headers)
                    {
                        context.Response.Headers[h.Key] = h.Value.ToArray();
                    }
                    foreach (var h in backupResponse.Content.Headers)
                    {
                        context.Response.Headers[h.Key] = h.Value.ToArray();
                    }

                    context.Response.Headers["X-Served-By"] = healthyDest.DestinationId;
                    context.Response.Headers["X-Auto-Failover"] = "true";

                    await backupResponse.Content.CopyToAsync(memStream, context.RequestAborted);
                }
            }
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(originalBody);
    }
    finally
    {
        context.Response.Body = originalBody;
    }
});

app.MapReverseProxy();
app.Run();
