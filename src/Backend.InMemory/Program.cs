using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Ten node: uu tien config "NodeName" (arg --NodeName), roi env NODE_NAME.
var nodeName = builder.Configuration["NodeName"]
    ?? Environment.GetEnvironmentVariable("NODE_NAME")
    ?? "node?";

// So giay auto-refresh trang HTML (chi de DEMO). 0 = tat. Doc tu Ui:RefreshSeconds.
var refreshSeconds = builder.Configuration.GetValue("Ui:RefreshSeconds", 2);

// Session luu IN-MEMORY (rieng tung node) -> co tinh minh hoa van de khi khong share state.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = ".Demo.Session";
    o.Cookie.HttpOnly = true;
    o.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHealthChecks();

// YARP gui X-Forwarded-* mac dinh; doc scheme/client IP dung khi LB HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseSession();

var startedAt = DateTimeOffset.Now;
long requestCount = 0;

app.MapHealthChecks("/health");

string TouchSession(HttpContext ctx)
{
    var firstSeen = ctx.Session.GetString("FirstSeen");
    if (firstSeen is null)
    {
        firstSeen = DateTimeOffset.Now.ToString("HH:mm:ss");
        ctx.Session.SetString("FirstSeen", firstSeen);
    }
    return firstSeen;
}

app.MapGet("/api/info", (HttpContext ctx) =>
{
    var count = Interlocked.Increment(ref requestCount);
    var firstSeen = TouchSession(ctx);
    return Results.Json(new
    {
        node = nodeName,
        hostname = Environment.MachineName,
        pid = Environment.ProcessId,
        requestCountOnThisNode = count,
        sessionId = ctx.Session.Id,
        sessionFirstSeenOnThisNode = firstSeen,
        startedAt
    });
});

app.MapGet("/", (HttpContext ctx) =>
{
    var count = Interlocked.Increment(ref requestCount);
    var firstSeen = TouchSession(ctx);
    var color = nodeName.Contains("1") ? "#2563eb" : "#16a34a";
    var refreshMeta = refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{refreshSeconds}\">" : "";
    var refreshHint = refreshSeconds > 0
        ? $"Trang tu refresh moi {refreshSeconds} giay (Ui:RefreshSeconds)."
        : "Auto-refresh TAT (Ui:RefreshSeconds=0).";
    var html = $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  {{refreshMeta}}
  <title>{{nodeName}}</title>
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin:0; height:100vh; display:grid; place-items:center; background:{{color}}; color:#fff; }
    .card { background: rgba(0,0,0,.22); padding:32px 56px; border-radius:18px; text-align:center; }
    h1 { font-size:72px; margin:0 0 8px; letter-spacing:2px; }
    table { margin:12px auto 0; border-collapse:collapse; font-size:15px; }
    td { padding:5px 14px; text-align:left; }
    td.k { opacity:.75; }
    .hint { opacity:.7; margin-top:18px; font-size:13px; }
  </style>
</head>
<body>
  <div class="card">
    <h1>{{nodeName}}</h1>
    <table>
      <tr><td class="k">Hostname</td><td>{{Environment.MachineName}}</td></tr>
      <tr><td class="k">PID</td><td>{{Environment.ProcessId}}</td></tr>
      <tr><td class="k">Request # (node nay)</td><td>{{count}}</td></tr>
      <tr><td class="k">Session Id</td><td>{{ctx.Session.Id}}</td></tr>
      <tr><td class="k">Session tao luc (node nay)</td><td>{{firstSeen}}</td></tr>
    </table>
    <p class="hint">{{refreshHint}} Xem header X-Served-By de biet LB chon node nao.</p>
  </div>
</body>
</html>
""";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();
