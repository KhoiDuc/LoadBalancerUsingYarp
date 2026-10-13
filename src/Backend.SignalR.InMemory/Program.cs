using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

var nodeName = builder.Configuration["NodeName"]
    ?? Environment.GetEnvironmentVariable("NODE_NAME")
    ?? "node?";

// SignalR IN-MEMORY (khong dung Redis Backplane) -> cac node khong noi chuyen duoc voi nhau!
builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();
app.UseForwardedHeaders();

app.MapHealthChecks("/health");

app.MapHub<ChatHub>("/chatHub");

app.MapGet("/", () =>
{
    var color = nodeName.Contains("1") ? "#2563eb" : "#16a34a";
    var html = $$"""
<!doctype html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <title>{{nodeName}} - SignalR (InMemory)</title>
  <style>
    body { font-family: system-ui, Arial, sans-serif; margin:0; height:100vh; display:grid; place-items:center; background:#111827; color:#fff; }
    .card { background:#1f2937; padding:24px 28px; border-radius:16px; width:480px; box-shadow:0 10px 25px rgba(0,0,0,.5); border-top:5px solid {{color}}; }
    .header { display:flex; justify-content:space-between; align-items:center; border-bottom:1px solid #374151; padding-bottom:12px; margin-bottom:14px; }
    .node-tag { background:{{color}}; color:#fff; padding:4px 12px; border-radius:12px; font-weight:700; font-size:13px; }
    .variant-tag { font-size:12px; color:#f87171; font-weight:600; }
    #messages { height:240px; overflow-y:auto; background:#111827; border-radius:10px; padding:12px; display:flex; flex-direction:column; gap:8px; border:1px solid #374151; }
    .msg { padding:6px 10px; border-radius:8px; font-size:13px; background:#374151; max-width:85%; word-break:break-word; }
    .msg .meta { font-size:11px; opacity:.7; margin-bottom:2px; }
    .msg.node1 { border-left:3px solid #3b82f6; }
    .msg.node2 { border-left:3px solid #22c55e; }
    .input-row { display:flex; gap:8px; margin-top:14px; }
    input { flex:1; padding:10px 14px; border-radius:8px; border:1px solid #4b5563; background:#111827; color:#fff; font-size:14px; }
    button { padding:10px 20px; border:0; border-radius:8px; background:{{color}}; color:#fff; font-weight:700; cursor:pointer; }
    .hint { font-size:12px; opacity:.75; margin-top:12px; line-height:1.4; color:#fca5a5; }
  </style>
  <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"></script>
</head>
<body>
  <div class="card">
    <div class="header">
      <div>
        <h2 style="margin:0; font-size:18px;">Demo Chat Realtime</h2>
        <span class="variant-tag">IN-MEMORY (Khong co Redis Backplane)</span>
      </div>
      <span class="node-tag">Ket noi: {{nodeName}}</span>
    </div>

    <div id="messages">
      <div class="msg" style="background:rgba(255,255,255,.05);"><span class="meta">He thong:</span> San sang ket noi. Hay mo 2 trinh duyet khac nhau (1 cai node1, 1 cai node2) de test.</div>
    </div>

    <div class="input-row">
      <input id="msgInput" placeholder="Nhap tin nhan..." autofocus>
      <button id="sendBtn">Gui</button>
    </div>

    <p class="hint">⚠️ <b>Hien tuong:</b> Vi luu In-Memory rieng tung node, tab o node1 chat thi tab o node2 <b>KHONG NHAN DUOC</b> tin nhan!</p>
  </div>

  <script>
    const currentNode = "{{nodeName}}";
    const conn = new signalR.HubConnectionBuilder()
      .withUrl("/chatHub")
      .withAutomaticReconnect()
      .build();

    const msgList = document.getElementById("messages");
    const msgInput = document.getElementById("msgInput");
    const sendBtn = document.getElementById("sendBtn");

    conn.on("ReceiveMessage", (user, text, node, time) => {
      const d = document.createElement("div");
      d.className = "msg " + (node === "node1" ? "node1" : "node2");
      d.innerHTML = `<div class="meta"><b>[${node}]</b> ${user} lúc ${time}</div><div>${text}</div>`;
      msgList.appendChild(d);
      msgList.scrollTop = msgList.scrollHeight;
    });

    conn.start().catch(e => console.error(e));

    function send() {
      const text = msgInput.value.trim();
      if (!text) return;
      conn.invoke("SendMessage", "Khach", text, currentNode);
      msgInput.value = "";
    }

    sendBtn.addEventListener("click", send);
    msgInput.addEventListener("keydown", (e) => { if (e.key === "Enter") send(); });
  </script>
</body>
</html>
""";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

public class ChatHub : Hub
{
    public async Task SendMessage(string user, string message, string node)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message, node, DateTime.Now.ToString("HH:mm:ss"));
    }
}
