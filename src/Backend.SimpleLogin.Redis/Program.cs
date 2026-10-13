using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var nodeName = builder.Configuration["NodeName"]
    ?? Environment.GetEnvironmentVariable("NODE_NAME")
    ?? "node?";
var refreshSeconds = builder.Configuration.GetValue("Ui:RefreshSeconds", 2);

var redisConn = builder.Configuration.GetConnectionString("Redis")
    ?? Environment.GetEnvironmentVariable("REDIS")
    ?? "localhost:6379";

var redis = await ConnectionMultiplexer.ConnectAsync(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

// ================== DIEM MAU CHOT ==================
// DataProtection DUNG CHUNG qua Redis + application name GIONG NHAU moi node:
//   - Cookie auth ma hoa boi node1 GIAI MA DUOC o node2.
//   - Hau qua: login o 1 node -> qua node khac VAN con dang nhap.
// ===================================================
builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(redis, "SimpleLogin-DP-Keys")
    .SetApplicationName("SimpleLogin"); // PHAI giong het nhau tren moi node.

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = ".SimpleLogin.Auth";
        o.Cookie.HttpOnly = true;
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddCheck("redis", () =>
    {
        try { redis.GetDatabase().Ping(); return HealthCheckResult.Healthy(); }
        catch (Exception ex) { return HealthCheckResult.Unhealthy(ex.Message); }
    });

// YARP gui X-Forwarded-* mac dinh; doc scheme/client IP dung khi LB HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapGet("/", (HttpContext ctx) =>
{
    var authed = ctx.User.Identity?.IsAuthenticated == true;
    var name = ctx.User.Identity?.Name ?? "-";
    var loginAt = ctx.User.FindFirst("LoginAt")?.Value ?? "-";
    var loginNode = ctx.User.FindFirst("LoginNode")?.Value ?? "-";
    var html = HomePage(nodeName, refreshSeconds, authed, name, loginAt, loginNode,
        "REDIS - DataProtection DUNG CHUNG");
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/login", () =>
    Results.Content(LoginPage(nodeName, refreshSeconds, null), "text/html; charset=utf-8"));

app.MapPost("/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var user = form["username"].ToString();
    var pass = form["password"].ToString();

    if (!string.IsNullOrWhiteSpace(user) && (pass == "1234" || (user == "admin" && pass == "admin")))
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user),
            new("LoginAt", DateTimeOffset.Now.ToString("HH:mm:ss")),
            new("LoginNode", nodeName),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/");
    }

    return Results.Content(
        LoginPage(nodeName, refreshSeconds, "Sai tai khoan/mat khau. Thu: username bat ky, password 1234."),
        "text/html; charset=utf-8");
});

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

app.MapGet("/api/whoami", (HttpContext ctx) => Results.Json(new
{
    node = nodeName,
    authenticated = ctx.User.Identity?.IsAuthenticated == true,
    user = ctx.User.Identity?.Name,
    loginNode = ctx.User.FindFirst("LoginNode")?.Value,
}));

app.Run();


static string HomePage(string node, int refreshSeconds, bool authed, string name,
    string loginAt, string loginNode, string variantLabel)
{
    var refreshMeta = refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{refreshSeconds + 1}\">" : "";
    var bg = authed ? "#16a34a" : "#b91c1c";
    var status = authed ? "DANG DANG NHAP" : "CHUA DANG NHAP (tren node nay)";
    var action = authed
        ? "<a class='btn' href='/logout'>Logout</a>"
        : "<a class='btn' href='/login'>Login</a>";
    var rows = authed
        ? $"<tr><td class='k'>User</td><td>{name}</td></tr>"
          + $"<tr><td class='k'>Login luc</td><td>{loginAt}</td></tr>"
          + $"<tr><td class='k'>Login tai node</td><td>{loginNode}</td></tr>"
        : "<tr><td colspan='2'>Node nay khong doc duoc cookie dang nhap.</td></tr>";
    var countdownHtml = refreshSeconds > 0
        ? $"<div class=\"countdown-badge\">Tu refresh sau: <span id=\"countdown\">{refreshSeconds}</span>s</div>"
        : "<div class=\"countdown-badge\">Auto-refresh: TAT</div>";
    return $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  {{refreshMeta}}
  <title>{{node}} - SimpleLogin (Redis)</title>
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin:0; height:100vh; display:grid; place-items:center; background:{{bg}}; color:#fff; }
    .card { background: rgba(0,0,0,.25); padding:28px 48px; border-radius:16px; text-align:center; min-width:340px; }
    .tag { font-size:12px; opacity:.85; letter-spacing:1px; }
    h1 { font-size:40px; margin:6px 0; }
    .status { font-size:20px; font-weight:700; margin:8px 0 14px; }
    table { margin:0 auto 14px; border-collapse:collapse; font-size:14px; }
    td { padding:4px 12px; text-align:left; }
    td.k { opacity:.75; }
    .countdown-badge { display:inline-block; background:rgba(0,0,0,.35); padding:6px 16px; border-radius:20px; font-size:13px; font-weight:600; margin-top:10px; border:1px solid rgba(255,255,255,.2); }
    .countdown-badge span { color:#fde047; font-size:16px; font-weight:700; }
    .btn { display:inline-block; margin-top:6px; padding:8px 20px; border-radius:8px; background:#fff; color:#111; text-decoration:none; font-weight:600; }
    .hint { opacity:.8; margin-top:14px; font-size:12px; max-width:360px; }
  </style>
</head>
<body>
  <div class="card">
    <div class="tag">{{variantLabel}}</div>
    <h1>{{node}}</h1>
    <div class="status">{{status}}</div>
    <table>{{rows}}</table>
    <div>{{action}}</div>
    {{countdownHtml}}
    <p class="hint">Trang tu refresh -> LB doi node1/node2. Key chia se qua Redis nen ca 2 node deu bao "DANG dang nhap".</p>
  </div>
  <script>
    (function () {
      var sec = {{refreshSeconds}};
      if (sec <= 0) return;
      var el = document.getElementById('countdown');
      var timer = setInterval(function () {
        sec--;
        if (el) el.textContent = Math.max(0, sec);
        if (sec <= 0) {
          clearInterval(timer);
          location.reload();
        }
      }, 1000);
    })();
  </script>
</body>
</html>
""";
}

static string LoginPage(string node, int refreshSeconds, string? error)
{
    var refreshMeta = refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{refreshSeconds + 1}\">" : "";
    var errHtml = error is null ? "" : $"<p class='err'>{error}</p>";
    var nodeColor = node.Contains("1") ? "#2563eb" : "#16a34a";
    var refreshHint = refreshSeconds > 0
        ? $"Tu refresh sau: <b id=\"countdown\" style=\"color:#60a5fa;\">{refreshSeconds}</b>s (form van giu gia tri)."
        : "Auto-refresh TAT (Ui:RefreshSeconds=0).";
    return $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  {{refreshMeta}}
  <title>Login - {{node}}</title>
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin:0; height:100vh; display:grid; place-items:center; background:#111827; color:#fff; }
    .card { background:#1f2937; padding:28px 32px; border-radius:14px; width:300px; display:flex; flex-direction:column; gap:10px; border-top:4px solid {{nodeColor}}; }
    h2 { margin:0 0 6px; font-size:20px; }
    .node { color:{{nodeColor}}; font-weight:700; }
    input { padding:10px 12px; border-radius:8px; border:1px solid #374151; background:#111827; color:#fff; font-size:14px; }
    button { padding:10px; border:0; border-radius:8px; background:#2563eb; color:#fff; font-weight:600; font-size:14px; cursor:pointer; }
    .err { color:#fca5a5; font-size:13px; margin:0; }
    .hint { opacity:.7; font-size:12px; margin:6px 0 0; }
  </style>
</head>
<body>
  <form class="card" method="post" action="/login" id="loginForm">
    <h2>Login (<span class="node">{{node}}</span>)</h2>
    {{errHtml}}
    <input id="username" name="username" placeholder="username" autofocus>
    <input id="password" name="password" type="password" placeholder="password">
    <button type="submit">Dang nhap</button>
    <p class="hint">Demo: username bat ky, password <b>1234</b> (hoac admin/admin).</p>
    <p class="hint">{{refreshHint}}</p>
  </form>
  <script>
    (function () {
      var sec = {{refreshSeconds}};
      if (sec > 0) {
        var el = document.getElementById('countdown');
        var timer = setInterval(function () {
          sec--;
          if (el) el.textContent = Math.max(0, sec);
          if (sec <= 0) {
            clearInterval(timer);
            location.reload();
          }
        }, 1000);
      }
      var u = document.getElementById('username');
      var p = document.getElementById('password');
      try {
        u.value = sessionStorage.getItem('simplelogin.user') || '';
        p.value = sessionStorage.getItem('simplelogin.pass') || '';
      } catch (e) {}
      function save() {
        try {
          sessionStorage.setItem('simplelogin.user', u.value);
          sessionStorage.setItem('simplelogin.pass', p.value);
        } catch (e) {}
      }
      u.addEventListener('input', save);
      p.addEventListener('input', save);
      document.getElementById('loginForm').addEventListener('submit', function () {
        try {
          sessionStorage.removeItem('simplelogin.user');
          sessionStorage.removeItem('simplelogin.pass');
        } catch (e) {}
      });
    })();
  </script>
</body>
</html>
""";
}
