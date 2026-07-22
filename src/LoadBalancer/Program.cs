using LoadBalancer;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // Chon luong theo LoadBalancer:Mode ("Stateless" | "Affinity").
    .AddConfigFilter<LoadBalancerModeConfigFilter>()
    // Cho biet node nao da phuc vu request -> rat tien khi test LB.
    .AddTransforms(ctx =>
    {
        ctx.AddResponseTransform(t =>
        {
            var dest = t.HttpContext.GetReverseProxyFeature().ProxiedDestination;
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

app.MapReverseProxy();
app.Run();
