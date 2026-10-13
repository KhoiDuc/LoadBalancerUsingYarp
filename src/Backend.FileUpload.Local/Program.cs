using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

var nodeName = builder.Configuration["NodeName"]
    ?? Environment.GetEnvironmentVariable("NODE_NAME")
    ?? "node?";

var refreshSeconds = builder.Configuration.GetValue("Ui:RefreshSeconds", 2);

// DIEM MAU CHOT: Luu file vao thu muc CUC BO rieng cua tung node
var uploadDir = Path.Combine(AppContext.BaseDirectory, "local_uploads");
Directory.CreateDirectory(uploadDir);

builder.Services.AddHealthChecks();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
app.UseForwardedHeaders();

app.MapHealthChecks("/health");

app.MapGet("/uploads/{fileName}", (string fileName) =>
{
    var filePath = Path.Combine(uploadDir, fileName);
    if (!File.Exists(filePath)) return Results.NotFound($"File {fileName} khong ton tai tren {nodeName}!");
    return Results.File(filePath, "image/jpeg");
});

app.MapPost("/upload", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files["avatar"];
    if (file is not null && file.Length > 0)
    {
        var targetPath = Path.Combine(uploadDir, "latest.jpg");
        using var stream = File.Create(targetPath);
        await file.CopyToAsync(stream);
    }
    return Results.Redirect("/");
});

app.MapGet("/", () =>
{
    var color = nodeName.Contains("1") ? "#2563eb" : "#16a34a";
    var hasFile = File.Exists(Path.Combine(uploadDir, "latest.jpg"));
    var fileStatusHtml = hasFile
        ? $"<div style='margin:10px 0;'><img src='/uploads/latest.jpg?t={DateTime.UtcNow.Ticks}' style='width:120px; height:120px; object-fit:cover; border-radius:12px; border:2px solid #fff;'><p style='font-size:12px; margin:4px 0 0;'>File CO SAN tren <b>{nodeName}</b></p></div>"
        : $"<div style='margin:10px 0; background:rgba(239,68,68,.2); padding:16px; border-radius:12px; border:1px dashed #ef4444;'><p style='margin:0; font-weight:700; color:#fca5a5;'>❌ KHONG TIM THAY FILE TREN {nodeName.ToUpper()}!</p><p style='margin:4px 0 0; font-size:12px; opacity:.8;'>File luu o node khac nen node nay khong doc duoc.</p></div>";

    var refreshMeta = refreshSeconds > 0 ? $"<meta http-equiv=\"refresh\" content=\"{refreshSeconds + 1}\">" : "";
    var countdownHtml = refreshSeconds > 0
        ? $"<div class=\"countdown-badge\">Tu refresh sau: <span id=\"countdown\">{refreshSeconds}</span>s</div>"
        : "<div class=\"countdown-badge\">Auto-refresh: TAT</div>";

    var html = $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  {{refreshMeta}}
  <title>{{nodeName}} - FileUpload (Local)</title>
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin:0; height:100vh; display:grid; place-items:center; background:{{color}}; color:#fff; }
    .card { background: rgba(0,0,0,.3); padding:28px 36px; border-radius:16px; text-align:center; width:400px; box-shadow:0 10px 25px rgba(0,0,0,.4); }
    h1 { font-size:42px; margin:4px 0 10px; }
    .tag { font-size:12px; font-weight:700; color:#fca5a5; letter-spacing:1px; }
    .countdown-badge { display:inline-block; background:rgba(0,0,0,.4); padding:6px 14px; border-radius:20px; font-size:13px; font-weight:600; margin:10px 0; }
    .countdown-badge span { color:#fde047; font-size:16px; font-weight:700; }
    form { margin-top:14px; background:rgba(0,0,0,.25); padding:14px; border-radius:12px; display:flex; flex-direction:column; gap:8px; }
    input[type=file] { font-size:13px; }
    button { padding:8px 16px; border:0; border-radius:8px; background:#fff; color:#111; font-weight:700; cursor:pointer; }
    .hint { font-size:12px; opacity:.85; margin-top:14px; line-height:1.4; }
  </style>
</head>
<body>
  <div class="card">
    <div class="tag">LOCAL STORAGE (Thu muc rieng tung node)</div>
    <h1>{{nodeName}}</h1>
    {{countdownHtml}}

    {{fileStatusHtml}}

    <form method="post" action="/upload" enctype="multipart/form-data">
      <input type="file" name="avatar" accept="image/*" required>
      <button type="submit">Upload Anh</button>
    </form>

    <p class="hint">⚠️ <b>Hien tuong:</b> Upload anh tai node1 -> Sau 2 giay trang doi sang node2 -> <b>Anh bi loi / khong tim thay tren node2</b> vi o dia khong dung chung!</p>
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
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();
