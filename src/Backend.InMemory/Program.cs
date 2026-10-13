using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Ten node: uu tien config "NodeName" (arg --NodeName), roi env NODE_NAME.
var nodeName = builder.Configuration["NodeName"]
    ?? Environment.GetEnvironmentVariable("NODE_NAME")
    ?? "node?";

// So giay auto-refresh trang HTML (chi de DEMO). 0 = tat. Doc tu Ui:RefreshSeconds.
var refreshSeconds = builder.Configuration.GetValue("Ui:RefreshSeconds", 3);

// Session luu IN-MEMORY (rieng tung node) -> co tinh minh hoa van de khi khong share state.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = ".Demo.Session";
    o.Cookie.HttpOnly = true;
    o.IdleTimeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHealthChecks();

// Background Job KHONG CO DISTRIBUTED LOCK -> ca 2 node cung chay, trung lap don hang!
builder.Services.AddHostedService<InMemoryOrderJobService>();

// YARP gui X-Forwarded-* mac dinh; doc scheme/client IP dung khi LB HTTPS.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
app.UseForwardedHeaders();

var startedAt = DateTimeOffset.Now;
long requestCount = 0;
var isHealthy = true;
var simMode = "Normal"; // "Normal" | "Slow" | "Error503"
var delayMs = 3000;

// Middleware mo phong loi hoac do tre
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/simulate") ||
        context.Request.Path.StartsWithSegments("/api/toggle-health") ||
        context.Request.Path.StartsWithSegments("/api/cart") ||
        context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    if (simMode == "Error503")
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Service Unavailable (Simulated Error 503 on node)");
        return;
    }

    if (simMode == "Slow")
    {
        await Task.Delay(delayMs, context.RequestAborted);
    }

    await next();
});

app.UseSession();

app.MapGet("/health", () => (isHealthy && simMode != "Error503")
    ? Results.Ok(new { status = "Healthy", node = nodeName, simMode })
    : Results.StatusCode(503));

app.MapPost("/api/toggle-health", () =>
{
    isHealthy = !isHealthy;
    return Results.Redirect("/");
});

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

// =========================================================================
// 1️⃣ FLOW 1: GIO HANG FORM SUBMIT (RELOAD TOAN BO TRANG NGAY LAP TUC)
// =========================================================================
app.MapPost("/api/cart/flow1/add", (HttpContext ctx) =>
{
    var current = ctx.Session.GetInt32("CartFlow1") ?? 0;
    ctx.Session.SetInt32("CartFlow1", current + 1);
    TouchSession(ctx);
    return Results.Redirect("/");
});

app.MapPost("/api/cart/flow1/clear", (HttpContext ctx) =>
{
    ctx.Session.SetInt32("CartFlow1", 0);
    return Results.Redirect("/");
});

// =========================================================================
// 2️⃣ FLOW 2: GIO HANG AJAX BAN LIEN TUC (HIEN LOG LIVE PHAN LUONG)
// =========================================================================
app.MapPost("/api/cart/flow2/add", (HttpContext ctx) =>
{
    var current = ctx.Session.GetInt32("CartFlow2") ?? 0;
    current++;
    ctx.Session.SetInt32("CartFlow2", current);
    var firstSeen = TouchSession(ctx);
    return Results.Json(new
    {
        success = true,
        cartCount = current,
        node = nodeName,
        sessionId = ctx.Session.Id,
        sessionFirstSeen = firstSeen,
        time = DateTime.Now.ToString("HH:mm:ss.fff")
    });
});

app.MapPost("/api/cart/flow2/clear", (HttpContext ctx) =>
{
    ctx.Session.SetInt32("CartFlow2", 0);
    return Results.Json(new { success = true, cartCount = 0, node = nodeName });
});

// =========================================================================
// 3️⃣ FLOW 3: GIO HANG AJAX TAI CHO + CHO AUTO-REFRESH SANG NODE KHAC
// =========================================================================
app.MapPost("/api/cart/flow3/add", (HttpContext ctx) =>
{
    var current = ctx.Session.GetInt32("CartFlow3") ?? 0;
    current++;
    ctx.Session.SetInt32("CartFlow3", current);
    var firstSeen = TouchSession(ctx);
    return Results.Json(new
    {
        success = true,
        cartCount = current,
        node = nodeName,
        sessionId = ctx.Session.Id,
        sessionFirstSeen = firstSeen,
        time = DateTime.Now.ToString("HH:mm:ss.fff")
    });
});

app.MapPost("/api/cart/flow3/clear", (HttpContext ctx) =>
{
    ctx.Session.SetInt32("CartFlow3", 0);
    return Results.Json(new { success = true, cartCount = 0, node = nodeName });
});

// Backward compatibility
app.MapPost("/api/cart/add", (HttpContext ctx) => Results.Redirect("/api/cart/flow1/add"));
app.MapPost("/api/cart/clear", (HttpContext ctx) => Results.Redirect("/api/cart/flow1/clear"));

// Endpoint dat che do gia lap su co: Normal | Slow | Error503
app.MapPost("/api/simulate", (string? mode, int? delay) =>
{
    if (!string.IsNullOrWhiteSpace(mode))
    {
        simMode = mode switch
        {
            "Slow" => "Slow",
            "Error503" => "Error503",
            _ => "Normal"
        };
    }
    if (delay.HasValue && delay.Value > 0)
    {
        delayMs = delay.Value;
    }
    return Results.Ok(new { node = nodeName, mode = simMode, delayMs, isHealthy });
});

// Endpoint test Rate Limiting qua YARP Load Balancer
app.MapGet("/api/test-ratelimit", () =>
    Results.Ok(new { node = nodeName, time = DateTime.Now.ToString("HH:mm:ss.fff") }));

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
        cartFlow1 = ctx.Session.GetInt32("CartFlow1") ?? 0,
        cartFlow2 = ctx.Session.GetInt32("CartFlow2") ?? 0,
        cartFlow3 = ctx.Session.GetInt32("CartFlow3") ?? 0,
        isHealthy,
        simMode,
        delayMs,
        lastJob = InMemoryOrderJobService.LastJobInfo,
        startedAt
    });
});

app.MapGet("/", (HttpContext ctx) =>
{
    var count = Interlocked.Increment(ref requestCount);
    var firstSeen = TouchSession(ctx);
    
    var cartFlow1 = ctx.Session.GetInt32("CartFlow1") ?? 0;
    var cartFlow2 = ctx.Session.GetInt32("CartFlow2") ?? 0;
    var cartFlow3 = ctx.Session.GetInt32("CartFlow3") ?? 0;

    var isNode1 = nodeName.Contains("1");
    var themeColor = isNode1 ? "#3b82f6" : "#10b981";
    var themeRgb = isNode1 ? "59, 130, 246" : "16, 185, 129";
    var port = isNode1 ? "5001" : "5002";
    
    var refreshMeta = refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{refreshSeconds + 1}\">" : "";
    var countdownHtml = refreshSeconds > 0
        ? $"Tự làm mới sau: <span id=\"countdown\" style=\"color:#fde047; font-weight:700;\">{refreshSeconds}</span>s"
        : "Tự làm mới: TẮT";
    
    var effectiveHealth = isHealthy && simMode != "Error503";
    var healthText = effectiveHealth ? "🟢 Khỏe mạnh (Healthy)" : "🔴 Đang sập (Unhealthy)";
    var simBadgeText = simMode switch
    {
        "Slow" => $"🟡 Đang bị nghẽn (Chậm {delayMs/1000}s)",
        "Error503" => "🔴 Đang trả lỗi HTTP 503",
        _ => "🟢 Phục vụ bình thường (0ms)"
    };

    var html = $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  {{refreshMeta}}
  <title>{{nodeName}} - Backend Node</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <style>
    :root {
      --theme: {{themeColor}};
      --theme-rgb: {{themeRgb}};
      --bg: #0b0f19;
      --card-bg: rgba(22, 30, 49, 0.85);
      --card-border: rgba(255, 255, 255, 0.08);
      --text-main: #f1f5f9;
      --text-muted: #94a3b8;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: 'Plus Jakarta Sans', system-ui, sans-serif;
      margin: 0; min-height: 100vh; display: grid; place-items: center;
      background: radial-gradient(circle at 50% 10%, rgba(var(--theme-rgb), 0.25) 0%, #0b0f19 50%, #050811 100%);
      color: var(--text-main); padding: 24px 16px;
    }
    .wrapper { width: 100%; max-width: 680px; display: flex; flex-direction: column; gap: 16px; }
    
    /* Top Routing Breadcrumb */
    .route-bar {
      background: rgba(15, 23, 42, 0.7); backdrop-filter: blur(10px);
      border: 1px solid var(--card-border); border-radius: 12px; padding: 12px 18px;
      font-size: 12px; display: flex; justify-content: space-between; align-items: center;
    }
    .route-flow { display: flex; align-items: center; gap: 8px; color: var(--text-muted); }
    .route-flow b { color: #fff; }
    .route-flow .active-node {
      color: var(--theme); font-weight: 700; background: rgba(var(--theme-rgb), 0.15);
      padding: 3px 10px; border-radius: 6px; border: 1px solid rgba(var(--theme-rgb), 0.3);
    }
    .countdown-pill {
      font-size: 11px; background: rgba(0,0,0,0.3); padding: 4px 12px; border-radius: 20px;
      border: 1px solid rgba(255,255,255,0.06);
    }

    /* Main Node Card */
    .card {
      background: var(--card-bg); backdrop-filter: blur(16px);
      border: 1px solid var(--card-border); border-top: 4px solid var(--theme);
      padding: 24px 28px; border-radius: 18px; box-shadow: 0 15px 35px rgba(0,0,0,0.4);
    }
    .node-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 18px; }
    .node-title-group h1 { font-size: 34px; font-weight: 800; letter-spacing: -1px; margin-bottom: 2px; }
    .node-title-group p { font-size: 12px; color: var(--text-muted); font-family: 'JetBrains Mono', monospace; }
    .status-badge {
      padding: 6px 14px; border-radius: 20px; font-size: 12px; font-weight: 700;
      background: rgba(var(--theme-rgb), 0.15); border: 1px solid rgba(var(--theme-rgb), 0.3);
      color: #fff;
    }

    /* SECTION 3 CUC GIO HANG */
    .cart-flows-section {
      background: rgba(15, 23, 42, 0.7); border: 1px solid var(--card-border);
      border-radius: 16px; padding: 18px; margin-bottom: 16px; display: flex; flex-direction: column; gap: 14px;
    }
    .section-header-row {
      display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid rgba(255,255,255,0.08); padding-bottom: 10px;
    }
    .section-header-title { font-size: 14px; font-weight: 800; color: #fff; }
    .flow-badge-inmemory { font-size: 11px; color: #f87171; font-weight: 700; }

    .flow-card {
      background: rgba(0,0,0,0.25); border: 1px solid var(--card-border);
      border-radius: 12px; padding: 14px 16px; display: flex; flex-direction: column; gap: 8px;
    }
    .flow-head { display: flex; justify-content: space-between; align-items: center; }
    .flow-title { font-size: 13px; font-weight: 700; color: #f1f5f9; display: flex; align-items: center; gap: 6px; }
    .flow-tag {
      font-size: 10px; font-weight: 700; padding: 2px 8px; border-radius: 10px;
      background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.12); color: var(--text-muted);
      font-family: 'JetBrains Mono', monospace;
    }
    .flow-desc { font-size: 11px; color: var(--text-muted); line-height: 1.4; }
    
    .flow-cart-row {
      display: flex; align-items: center; justify-content: space-between;
      background: rgba(0,0,0,0.2); padding: 8px 12px; border-radius: 8px;
    }
    .flow-count { font-size: 14px; color: #cbd5e1; }
    .flow-count b { color: #fde047; font-size: 17px; font-family: 'JetBrains Mono', monospace; }
    
    .flow-btns { display: flex; gap: 6px; flex-wrap: wrap; }
    .btn-action {
      padding: 6px 12px; border-radius: 6px; font-weight: 700; font-size: 11px;
      border: 0; cursor: pointer; transition: all 0.2s; display: inline-flex; align-items: center; gap: 4px;
    }
    .btn-add { background: var(--theme); color: #fff; }
    .btn-add:hover { opacity: 0.9; }
    .btn-clear { background: rgba(255,255,255,0.08); color: var(--text-muted); }
    .btn-clear:hover { background: rgba(255,255,255,0.15); color: #fff; }

    /* Alert Boxes */
    .explain-flow3 {
      font-size: 11px; padding: 8px 12px; border-radius: 6px; line-height: 1.4;
      background: rgba(239, 68, 68, 0.12); border-left: 3px solid #ef4444; color: #fca5a5;
    }

    /* Telemetry Table */
    table { width: 100%; border-collapse: collapse; font-size: 13px; margin-bottom: 16px; }
    td { padding: 6px 10px; border-bottom: 1px solid rgba(255,255,255,0.04); }
    td.k { color: var(--text-muted); width: 45%; }
    td.v { font-family: 'JetBrains Mono', monospace; font-weight: 600; }

    /* Failure Injection Section */
    .sim-section {
      background: rgba(0,0,0,0.25); border: 1px solid var(--card-border);
      border-radius: 14px; padding: 14px 18px; margin-bottom: 16px;
    }
    .sim-title { font-size: 12px; font-weight: 700; text-transform: uppercase; color: var(--text-muted); margin-bottom: 8px; }
    .sim-buttons { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
    .btn-sim {
      padding: 8px; border-radius: 8px; font-size: 11px; font-weight: 600;
      border: 1px solid var(--card-border); background: rgba(255,255,255,0.04); color: #fff;
      cursor: pointer; transition: all 0.2s; text-align: center;
    }
    .btn-sim.ok:hover { background: rgba(16, 185, 129, 0.25); border-color: #10b981; }
    .btn-sim.warn:hover { background: rgba(245, 158, 11, 0.25); border-color: #f59e0b; }
    .btn-sim.danger:hover { background: rgba(239, 68, 68, 0.25); border-color: #ef4444; }

    /* Background Job Box */
    .job-box {
      background: rgba(0,0,0,0.2); border: 1px solid var(--card-border);
      border-radius: 12px; padding: 12px 16px; font-size: 12px; line-height: 1.5;
    }
    .job-badge { color: #fca5a5; font-weight: 700; }

    /* Bottom Link */
    .bottom-bar { text-align: center; margin-top: 6px; }
    .dashboard-link {
      display: inline-flex; align-items: center; gap: 6px;
      background: rgba(255,255,255,0.08); color: #fff; text-decoration: none;
      padding: 8px 18px; border-radius: 20px; font-size: 12px; font-weight: 600;
      border: 1px solid var(--card-border); transition: all 0.2s;
    }
    .dashboard-link:hover { background: rgba(255,255,255,0.15); transform: translateY(-1px); }
  </style>
</head>
<body>
  <div class="wrapper">
    
    <!-- Top Route Breadcrumb -->
    <div class="route-bar">
      <div class="route-flow">
        <span>Trình duyệt</span>
        <span>➔</span>
        <span>Gateway <b>:5266</b></span>
        <span>➔</span>
        <span class="active-node">Đang ở: {{nodeName}} (:{{port}})</span>
      </div>
      <div class="countdown-pill">{{countdownHtml}}</div>
    </div>

    <!-- Main Card -->
    <div class="card">
      <div class="node-header">
        <div class="node-title-group">
          <h1>{{nodeName}}</h1>
          <p>http://127.0.0.1:{{port}} (PID: {{Environment.ProcessId}})</p>
        </div>
        <div class="status-badge">{{healthText}}</div>
      </div>

      <!-- ============================================================== -->
      <!-- SECTION 3 CUC GIO HANG DANG 3 FLOW RIENG BIET                     -->
      <!-- ============================================================== -->
      <div class="cart-flows-section">
        <div class="section-header-row">
          <div class="section-header-title">🛒 3 Thử Nghiệm Giỏ Hàng (3 Flow Riêng Biệt)</div>
          <div class="flow-badge-inmemory">Chế độ: IN-MEMORY (RAM riêng)</div>
        </div>

        <!-- CUC 1: FLOW 1 -->
        <div class="flow-card">
          <div class="flow-head">
            <div class="flow-title">1️⃣ Flow 1: Form POST (Tải Lại Trang Ngay)</div>
            <span class="flow-tag">Full-page Reload</span>
          </div>
          <div class="flow-desc">Submit Form truyền thống. Bấm là trình duyệt redirect làm mới cả trang sang node mới ngay lập tức.</div>
          <div class="flow-cart-row">
            <div class="flow-count">Giỏ hàng Flow 1: <b>{{cartFlow1}} món</b></div>
            <div class="flow-btns">
              <form method="post" action="/api/cart/flow1/add" style="margin:0;">
                <button class="btn-action btn-add" type="submit">+ Thêm 1 Món (Reload)</button>
              </form>
              <form method="post" action="/api/cart/flow1/clear" style="margin:0;">
                <button class="btn-action btn-clear" type="submit">🗑️ Xóa</button>
              </form>
            </div>
          </div>
        </div>

        <!-- CUC 2: FLOW 2 -->
        <div class="flow-card">
          <div class="flow-head">
            <div class="flow-title">2️⃣ Flow 2: AJAX Bắn Liên Tục (Hiện Log Phân Luồng)</div>
            <span class="flow-tag" style="background:rgba(139,92,246,0.2); color:#c4b5fd; border-color:rgba(139,92,246,0.3);">AJAX Stream</span>
          </div>
          <div class="flow-desc">Mô phỏng SPA gọi fetch ngầm liên tục: Quan sát từng request rơi vào node nào và xem giỏ hàng có bị lệch không.</div>
          <div class="flow-btns">
            <button class="btn-action" style="background:#8b5cf6; color:#fff;" type="button" onclick="callFlow2('add')">⚡ Thêm 1 Món</button>
            <button class="btn-action" style="background:#6366f1; color:#fff;" type="button" onclick="runFlow2Multi(4)">🚀 Bắn 4 Request Liên Tục</button>
            <button class="btn-action btn-clear" type="button" onclick="callFlow2('clear')">🗑️ Reset</button>
          </div>
          <div id="flow2LogWrap" style="background:rgba(0,0,0,0.35); border-radius:8px; padding:8px 10px; font-family:'JetBrains Mono',monospace; font-size:11px; max-height:110px; overflow-y:auto; display:none; border:1px solid rgba(255,255,255,0.06);">
            <div id="flow2LogsList"></div>
          </div>
          <div id="flow2Feedback" style="margin-top:4px; font-size:11px; display:none;"></div>
        </div>

        <!-- CUC 3: FLOW 3 -->
        <div class="flow-card">
          <div class="flow-head">
            <div class="flow-title">3️⃣ Flow 3: AJAX Tại Chỗ ➔ Chờ Auto-Refresh Đổi Node</div>
            <span class="flow-tag" style="background:rgba(16,185,129,0.2); color:#6ee7b7; border-color:rgba(16,185,129,0.3);">AJAX + Countdown</span>
          </div>
          <div class="flow-desc">Bấm thêm món bằng AJAX tại {{nodeName}} (không reload ngay), sau đó chờ hết countdown đếm ngược để trang tự làm mới sang node khác.</div>
          <div class="flow-cart-row">
            <div class="flow-count">Giỏ hàng Flow 3: <b id="flow3CountNum">{{cartFlow3}} món</b></div>
            <div class="flow-btns">
              <button class="btn-action btn-add" type="button" onclick="addFlow3Item()">⚡ + Thêm 1 Món (Tại chỗ)</button>
              <button class="btn-action btn-clear" type="button" onclick="clearFlow3()">🗑️ Xóa</button>
            </div>
          </div>
          <div id="flow3StatusMsg" style="font-size:11px; color:#38bdf8; min-height:16px;"></div>
          <div id="flow3ExplainBox" class="explain-flow3">
            💡 <b>Bài thử Flow 3:</b> Bấm "+ Thêm 1 Món" 1 hoặc 2 lần (số sẽ tăng ngay tại chỗ). Sau đó chờ countdown hết giờ để trang tự refresh sang node kia ➔ Xem giỏ hàng có giữ được 2 món không!
          </div>
        </div>

      </div>

      <!-- Node Telemetry Table -->
      <table>
        <tr><td class="k">Máy chủ (Hostname)</td><td class="v">{{Environment.MachineName}}</td></tr>
        <tr><td class="k">Tiến trình (PID)</td><td class="v">{{Environment.ProcessId}}</td></tr>
        <tr><td class="k">Số request đã xử lý tại node này</td><td class="v">{{count}}</td></tr>
        <tr><td class="k">Mã Session (Cookie)</td><td class="v" style="font-size:11px;">{{ctx.Session.Id}}</td></tr>
        <tr><td class="k">Session tạo lúc (trên node này)</td><td class="v">{{firstSeen}}</td></tr>
        <tr><td class="k">Tình trạng giả lập hiện tại</td><td class="v">{{simBadgeText}}</td></tr>
      </table>

      <!-- Local Failure Injection -->
      <div class="sim-section">
        <div class="sim-title">⚡ Tạo sự cố thử nghiệm ngay tại node này:</div>
        <div class="sim-buttons">
          <button class="btn-sim ok" onclick="setSimMode('Normal')">🟢 Bình thường<br><span style="font-size:10px; color:#94a3b8;">0ms delay, 200 OK</span></button>
          <button class="btn-sim warn" onclick="setSimMode('Slow')">🟡 Làm nghẽn 3s<br><span style="font-size:10px; color:#94a3b8;">Chậm 3 giây</span></button>
          <button class="btn-sim danger" onclick="setSimMode('Error503')">🔴 Báo lỗi 503<br><span style="font-size:10px; color:#94a3b8;">Giả lập sập node</span></button>
        </div>
      </div>

      <!-- User Style Failover Test Box -->
      <div style="background: rgba(15, 23, 42, 0.85); border: 1px solid rgba(96, 165, 250, 0.35); border-radius: 12px; padding: 14px 18px; margin-top: 14px;">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:6px;">
          <span style="font-weight:700; color:#60a5fa; font-size:14px;">🛡️ Thử Nghiệm Tự Động Failover (Style F5 Trực Tiếp)</span>
          <span style="font-size:10px; background:#1e40af; color:#bfdbfe; padding:2px 8px; border-radius:10px; font-weight:700;">STYLE CỦA BẠN</span>
        </div>
        <p style="font-size:12px; color:#cbd5e1; line-height:1.5; margin-bottom:10px;">
          {{(isNode1 ? "Khi bạn bấm nút đỏ dưới đây, Node 1 sẽ trả lỗi 503. Bạn bấm F5 (refresh) liên tục thì Gateway sẽ <b>LUÔN LUÔN đưa bạn an toàn sang Node 2 (màu xanh lá, 200 OK)</b>, tuyệt đối không bao giờ thấy lỗi 503!" : "Bạn đang ở Node 2 (Node dự phòng an toàn)! Kể cả Node 1 bị sập lỗi 503 thì mọi lượt F5 của bạn đều được Load Balancer bảo vệ và giữ an toàn tại đây (200 OK).")}}
        </p>
        <div style="display:flex; gap:8px;">
          {{(isNode1 
            ? "<button class=\"btn-sim danger\" style=\"flex:1; padding:10px; font-weight:700;\" onclick=\"setSimMode('Error503')\">🔴 Đánh Sập Node 1 (Báo 503) ➔ F5 Sang Node 2</button>"
            : "<button class=\"btn-sim ok\" style=\"flex:1; padding:10px; font-weight:700;\" onclick=\"setSimMode('Normal')\">🟢 Khôi Phục Node Này Về Bình Thường</button>")}}
          <button class="btn-sim" style="flex:1; background:#1e293b; border:1px solid #334155; padding:10px; color:#93c5fd; font-weight:700;" onclick="location.reload()">🔄 Thử F5 (Refresh) Ngay</button>
        </div>
      </div>

      <!-- Background Job simulation -->
      <div class="job-box">
        <div style="font-weight:700; margin-bottom:2px;">⚙️ Background Worker: <span class="job-badge">KHÔNG LOCK (Chạy trùng lặp!)</span></div>
        <div style="color:var(--text-muted);">Mã đơn hàng vừa nhặt: <b style="color:#fff;">{{InMemoryOrderJobService.LastJobInfo}}</b></div>
        <div style="font-size:11px; color:#fca5a5; margin-top:4px;">
          ⚠️ <b>Vấn đề:</b> Cả 2 node đang cùng xử lý đúng một mã đơn hàng này vì không có Distributed Lock phối hợp!
        </div>
      </div>
    </div>

    <!-- Bottom Gateway Dashboard Link -->
    <div class="bottom-bar">
      <a href="http://localhost:5266/dashboard" class="dashboard-link">📊 Mở YARP Control Center Dashboard (:5266/dashboard) &rarr;</a>
    </div>

  </div>

  <script>
    const serverFlow3Count = {{cartFlow3}};
    const currentNode = "{{nodeName}}";

    // Auto-refresh countdown timer
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

    // Check Flow 3 Hop from previous node
    (function checkFlow3() {
      const prevAdded = parseInt(sessionStorage.getItem('flow3LastAdded') || '0', 10);
      const prevNode = sessionStorage.getItem('flow3LastNode');
      const box = document.getElementById('flow3ExplainBox');

      if (prevAdded > 0 && prevNode && prevNode !== currentNode && serverFlow3Count === 0) {
        box.innerHTML = `⚠️ <b>PHÁT HIỆN MẤT DỮ LIỆU:</b> Ở <b>${prevNode}</b> bạn vừa thêm <b>${prevAdded} món</b>, nhưng khi countdown tự refresh sang <b>${currentNode}</b> thì giỏ hàng bị về <b>0 món</b>! Vì ${currentNode} không thể đọc Session RAM của ${prevNode}!`;
      }
    })();

    // FLOW 3: AJAX Add in-place then wait for countdown
    async function addFlow3Item() {
      const msg = document.getElementById('flow3StatusMsg');
      try {
        const res = await fetch('/api/cart/flow3/add', { method: 'POST' });
        const data = await res.json();
        document.getElementById('flow3CountNum').textContent = data.cartCount + ' món';
        sessionStorage.setItem('flow3LastAdded', data.cartCount);
        sessionStorage.setItem('flow3LastNode', currentNode);
        msg.textContent = `✨ Đã thêm ${data.cartCount} món tại ${currentNode}! Hãy chờ countdown đếm ngược để trang tự refresh sang node kia...`;
      } catch (e) { console.error(e); }
    }

    async function clearFlow3() {
      try {
        await fetch('/api/cart/flow3/clear', { method: 'POST' });
        document.getElementById('flow3CountNum').textContent = '0 món';
        sessionStorage.removeItem('flow3LastAdded');
        sessionStorage.removeItem('flow3LastNode');
        document.getElementById('flow3StatusMsg').textContent = '🗑️ Đã xóa giỏ hàng Flow 3.';
      } catch (e) { console.error(e); }
    }

    // FLOW 2: AJAX Multi-call stream log
    let flow2Count = 0;
    let flow2LastNode = null;
    let flow2Mismatch = false;

    async function callFlow2(action) {
      const wrap = document.getElementById("flow2LogWrap");
      const list = document.getElementById("flow2LogsList");
      const fb = document.getElementById("flow2Feedback");
      wrap.style.display = "block";

      flow2Count++;
      const url = action === 'clear' ? '/api/cart/flow2/clear' : '/api/cart/flow2/add';
      try {
        const res = await fetch(url, { method: 'POST' });
        const data = await res.json();

        const nodeColor = data.node.includes('1') ? '#60a5fa' : '#34d399';
        const isSwitched = flow2LastNode && flow2LastNode !== data.node;
        if (isSwitched) flow2Mismatch = true;
        flow2LastNode = data.node;

        const row = document.createElement("div");
        row.style.padding = "3px 0";
        row.style.borderBottom = "1px solid rgba(255,255,255,0.05)";
        row.innerHTML = `[${data.time || ''}] #${flow2Count} ➔ <b style="color:${nodeColor}">${data.node}</b> | Giỏ: <b style="color:#fde047">${data.cartCount} món</b> ${isSwitched ? '<span style="color:#f87171; font-weight:700;">(ĐỔI NODE!)</span>' : ''}`;
        list.prepend(row);

        fb.style.display = "block";
        if (flow2Mismatch) {
          fb.innerHTML = `<span style="color:#f87171;">⚠️ <b>LỆCH STATE AJAX:</b> Các request fetch liên tiếp rơi vào các node khác nhau nên giỏ hàng bị lệch do mỗi node InMemory lưu RAM riêng!</span>`;
        }
      } catch (e) { console.error(e); }
    }

    async function runFlow2Multi(times) {
      for (let i = 0; i < times; i++) {
        await callFlow2('add');
        await new Promise(r => setTimeout(r, 250));
      }
    }

    async function setSimMode(mode) {
      await fetch(`/api/simulate?mode=${mode}&delay=3000`, { method: 'POST' });
      try {
        await fetch(`/api/lb/simulate-node?node={{nodeName}}&mode=${mode}&delayMs=3000`, { method: 'POST' });
      } catch (e) {}
      setTimeout(() => location.reload(), 150);
    }
  </script>
</body>
</html>
""";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

public class InMemoryOrderJobService(ILogger<InMemoryOrderJobService> logger) : BackgroundService
{
    public static string LastJobInfo = "Chưa chạy";
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
            long timeSlot = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 5;
            var orderCode = $"ORD-{(1000 + (timeSlot % 9000))}";
            LastJobInfo = $"Đơn #{orderCode} lúc {DateTime.Now:HH:mm:ss}";
            logger.LogInformation("Job chạy ngầm KHÔNG LOCK (trùng lặp): {Job}", LastJobInfo);
        }
    }
}
