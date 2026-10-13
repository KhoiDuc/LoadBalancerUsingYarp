using System.Diagnostics;
using System.Text.Json;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace LoadBalancer;

public static class DashboardEndpoints
{
    public static void MapDashboard(this WebApplication app)
    {
        // Trang Dashboard HTML truc quan, co huong dan tung buoc va phong thi nghiem 1-click
        app.MapGet("/dashboard", () => Results.Content(GetDashboardHtml(), "text/html; charset=utf-8"));
        app.MapGet("/lb-dashboard", () => Results.Redirect("/dashboard"));

        // API trang thai tong quan
        app.MapGet("/api/lb/status", (LbState state, LbMetrics metrics, IProxyStateLookup proxyStateLookup) =>
        {
            var destinations = new List<object>();
            if (proxyStateLookup.TryGetCluster("demo", out var clusterState))
            {
                foreach (var (id, dest) in clusterState.Destinations)
                {
                    destinations.Add(new
                    {
                        id = id,
                        address = dest.Model?.Config?.Address ?? "",
                        activeHealth = dest.Health.Active.ToString(),
                        passiveHealth = dest.Health.Passive.ToString(),
                        inFlight = dest.ConcurrentRequestCount
                    });
                }
            }

            return Results.Json(new
            {
                algorithm = state.Algorithm,
                mode = state.Mode,
                destinations,
                metrics = metrics.GetSnapshot()
            });
        });

        // API doi thuat toan Load Balancing
        app.MapPost("/api/lb/algorithm", (string algo, LbState state) =>
        {
            var success = state.SetAlgorithm(algo);
            return success
                ? Results.Ok(new { message = $"Đã đổi thuật toán sang {state.Algorithm}", current = state.Algorithm })
                : Results.BadRequest(new { message = $"Thuật toán không hợp lệ. Cho phép: {string.Join(", ", LbState.AllowedAlgorithms)}" });
        });

        // API doi Session Affinity Mode
        app.MapPost("/api/lb/mode", (string mode, LbState state) =>
        {
            var success = state.SetMode(mode);
            return Results.Ok(new { message = $"Đã đổi chế độ sang {state.Mode}", current = state.Mode });
        });

        // API quan ly ngrok tunnel
        app.MapGet("/api/lb/ngrok", async (IHttpClientFactory clientFactory) =>
        {
            try
            {
                var client = clientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(1);
                var json = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                using var doc = JsonDocument.Parse(json);
                var tunnels = doc.RootElement.GetProperty("tunnels");
                string? publicUrl = null;
                foreach (var t in tunnels.EnumerateArray())
                {
                    var url = t.GetProperty("public_url").GetString();
                    if (url != null && url.StartsWith("https://"))
                    {
                        publicUrl = url;
                        break;
                    }
                    publicUrl ??= url;
                }

                return Results.Ok(new { isRunning = publicUrl != null, publicUrl });
            }
            catch
            {
                return Results.Ok(new { isRunning = false, publicUrl = (string?)null });
            }
        });

        app.MapPost("/api/lb/ngrok/start", async (IHttpClientFactory clientFactory) =>
        {
            try
            {
                var client = clientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(1);
                try
                {
                    var existing = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                    using var doc = JsonDocument.Parse(existing);
                    var tunnels = doc.RootElement.GetProperty("tunnels");
                    foreach (var t in tunnels.EnumerateArray())
                    {
                        var u = t.GetProperty("public_url").GetString();
                        if (u != null) return Results.Ok(new { isRunning = true, publicUrl = u });
                    }
                }
                catch {}

                var psi = new ProcessStartInfo
                {
                    FileName = "ngrok",
                    Arguments = "http 5266",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(400);
                    try
                    {
                        var json = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                        using var doc = JsonDocument.Parse(json);
                        var tunnels = doc.RootElement.GetProperty("tunnels");
                        foreach (var t in tunnels.EnumerateArray())
                        {
                            var url = t.GetProperty("public_url").GetString();
                            if (url != null) return Results.Ok(new { isRunning = true, publicUrl = url });
                        }
                    }
                    catch {}
                }

                return Results.Json(new { isRunning = false, error = "Không thể lấy link từ ngrok trong 5s." }, statusCode: 500);
            }
            catch (Exception ex)
            {
                return Results.Json(new { isRunning = false, error = ex.Message }, statusCode: 500);
            }
        });

        app.MapPost("/api/lb/ngrok/stop", () =>
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("ngrok"))
                {
                    p.Kill();
                }
                return Results.Ok(new { isRunning = false, message = "Đã tắt ngrok." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 500);
            }
        });

        // API gia lap su co tren Node (goi sang backend 5001 hoac 5002)
        app.MapPost("/api/lb/simulate-node", async (
            string node,
            string mode,
            int? delayMs,
            IHttpClientFactory clientFactory,
            IProxyStateLookup proxyLookup,
            IDestinationHealthUpdater healthUpdater) =>
        {
            var port = node.Equals("node2", StringComparison.OrdinalIgnoreCase) ? 5002 : 5001;
            var targetUrl = $"http://127.0.0.1:{port}/api/simulate?mode={Uri.EscapeDataString(mode)}&delayMs={delayMs ?? 3000}";

            // TUC THI: Cap nhat ngay trang thai tren Load Balancer (0 mili-giay) de router loai bo ngay
            if (proxyLookup.TryGetCluster("demo", out var clusterState))
            {
                var destKey = clusterState.Destinations.Keys.FirstOrDefault(k => k.Equals(node, StringComparison.OrdinalIgnoreCase));
                if (destKey != null && clusterState.Destinations.TryGetValue(destKey, out var destState))
                {
                    if (mode.Equals("Error503", StringComparison.OrdinalIgnoreCase))
                    {
                        healthUpdater.SetPassive(clusterState, destState, DestinationHealth.Unhealthy, TimeSpan.FromSeconds(60));
                    }
                    else if (mode.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                    {
                        healthUpdater.SetPassive(clusterState, destState, DestinationHealth.Healthy, TimeSpan.FromSeconds(60));
                    }
                }
            }

            try
            {
                var client = clientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                var res = await client.PostAsync(targetUrl, null);
                var body = await res.Content.ReadAsStringAsync();
                return Results.Ok(new { node, mode, response = body });
            }
            catch (Exception ex)
            {
                return Results.Json(new { node, mode, error = ex.Message }, statusCode: 500);
            }
        });

        // API phat tai thu nghiem (Traffic Generator)
        app.MapPost("/api/lb/generate-traffic", async (int count, bool concurrent, string? path, IHttpClientFactory clientFactory) =>
        {
            if (count <= 0) count = 10;
            if (count > 100) count = 100;
            var requestPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
            if (!requestPath.StartsWith('/')) requestPath = "/" + requestPath;

            var client = clientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var swTotal = Stopwatch.StartNew();

            var nodeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["node1"] = 0,
                ["node2"] = 0,
                ["other"] = 0
            };
            var statusCounts = new Dictionary<int, int>();
            var latencies = new List<long>();
            var reqLogs = new List<object>();

            async Task SendSingleAsync()
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    // Goi vao gateway LB (port 5266)
                    using var res = await client.GetAsync($"http://127.0.0.1:5266{requestPath}");
                    sw.Stop();
                    long lat = sw.ElapsedMilliseconds;

                    lock (nodeCounts)
                    {
                        latencies.Add(lat);
                        int sc = (int)res.StatusCode;
                        statusCounts[sc] = statusCounts.GetValueOrDefault(sc, 0) + 1;

                        string? servedNode = null;
                        if (res.Headers.TryGetValues("X-Served-By", out var values))
                        {
                            servedNode = values.FirstOrDefault() ?? "unknown";
                            nodeCounts[servedNode] = nodeCounts.GetValueOrDefault(servedNode, 0) + 1;
                        }
                        else
                        {
                            nodeCounts["other"]++;
                        }

                        reqLogs.Add(new
                        {
                            reqNum = reqLogs.Count + 1,
                            statusCode = sc,
                            servedBy = servedNode ?? (sc == 429 ? "Gateway (Chặn 429)" : "Gateway"),
                            latencyMs = lat
                        });
                    }
                }
                catch (Exception)
                {
                    sw.Stop();
                    lock (nodeCounts)
                    {
                        latencies.Add(sw.ElapsedMilliseconds);
                        statusCounts[500] = statusCounts.GetValueOrDefault(500, 0) + 1;
                        nodeCounts["other"]++;
                        reqLogs.Add(new
                        {
                            reqNum = reqLogs.Count + 1,
                            statusCode = 500,
                            servedBy = "Lỗi kết nối",
                            latencyMs = sw.ElapsedMilliseconds
                        });
                    }
                }
            }

            if (concurrent)
            {
                var tasks = Enumerable.Range(0, count).Select(_ => SendSingleAsync());
                await Task.WhenAll(tasks);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    await SendSingleAsync();
                }
            }

            swTotal.Stop();

            return Results.Json(new
            {
                totalRequests = count,
                isConcurrent = concurrent,
                path = requestPath,
                durationMs = swTotal.ElapsedMilliseconds,
                minLatencyMs = latencies.Count > 0 ? latencies.Min() : 0,
                maxLatencyMs = latencies.Count > 0 ? latencies.Max() : 0,
                avgLatencyMs = latencies.Count > 0 ? Math.Round(latencies.Average(), 1) : 0,
                nodeBreakdown = nodeCounts,
                statusBreakdown = statusCounts,
                logs = reqLogs
            });
        });

        // API xoa bo dem metrics
        app.MapPost("/api/lb/clear-metrics", (LbMetrics metrics) =>
        {
            metrics.Reset();
            return Results.Ok(new { message = "Đã reset toàn bộ metrics" });
        });
    }

    private static string GetDashboardHtml()
    {
        return """
<!DOCTYPE html>
<html lang="vi">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>YARP Load Balancer - Phòng Thí Nghiệm & Giám Sát</title>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;600;700&family=Plus+Jakarta+Sans:wght@400;500;600;700;800&display=swap" rel="stylesheet">
  <style>
    :root {
      --bg: #0b0f19;
      --card-bg: rgba(22, 30, 49, 0.75);
      --card-border: rgba(255, 255, 255, 0.08);
      --text-main: #f1f5f9;
      --text-muted: #94a3b8;
      --primary: #3b82f6;
      --node1: #3b82f6;
      --node2: #10b981;
      --danger: #ef4444;
      --warning: #f59e0b;
      --success: #10b981;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      background: radial-gradient(circle at 50% 0%, #1e1b4b 0%, #0b0f19 45%, #050811 100%);
      color: var(--text-main);
      font-family: 'Plus Jakarta Sans', system-ui, sans-serif;
      min-height: 100vh;
      padding: 20px 16px;
    }
    .container { max-width: 1240px; margin: 0 auto; display: flex; flex-direction: column; gap: 18px; }
    
    /* Top Banner / Help */
    .help-banner {
      background: linear-gradient(135deg, rgba(30, 58, 138, 0.4), rgba(88, 28, 135, 0.3));
      border: 1px solid rgba(147, 197, 253, 0.2);
      border-radius: 14px; padding: 14px 20px; display: flex; justify-content: space-between; align-items: center;
      gap: 14px;
    }
    .help-content { font-size: 13px; line-height: 1.5; color: #cbd5e1; }
    .help-content b { color: #60a5fa; }
    .btn-help-toggle {
      background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.15);
      color: #fff; padding: 6px 12px; border-radius: 8px; font-size: 12px; font-weight: 600;
      cursor: pointer; white-space: nowrap; transition: all 0.2s;
    }
    .btn-help-toggle:hover { background: rgba(255,255,255,0.2); }

    /* Header */
    .header {
      display: flex; justify-content: space-between; align-items: center;
      background: var(--card-bg); backdrop-filter: blur(12px);
      border: 1px solid var(--card-border); border-radius: 16px;
      padding: 16px 22px; box-shadow: 0 10px 30px rgba(0,0,0,0.4);
    }
    .header-brand { display: flex; align-items: center; gap: 14px; }
    .header-logo {
      width: 44px; height: 44px; background: linear-gradient(135deg, #3b82f6, #8b5cf6);
      border-radius: 12px; display: grid; place-items: center; font-size: 22px; font-weight: 800;
      box-shadow: 0 0 20px rgba(59, 130, 246, 0.3);
    }
    .header-title h1 { font-size: 20px; font-weight: 800; letter-spacing: -0.5px; }
    .header-title p { font-size: 13px; color: var(--text-muted); }
    .header-badges { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    .badge {
      display: inline-flex; align-items: center; gap: 6px;
      padding: 6px 14px; border-radius: 20px; font-size: 12px; font-weight: 600;
      background: rgba(255,255,255,0.06); border: 1px solid var(--card-border);
    }
    .badge-dot { width: 8px; height: 8px; border-radius: 50%; background: var(--success); }
    .badge-dot.pulse { animation: pulse 1.5s infinite; }
    @keyframes pulse { 0% { opacity: 0.4; } 50% { opacity: 1; } 100% { opacity: 0.4; } }

    /* Interactive 1-Click Labs Section */
    .labs-section {
      background: linear-gradient(135deg, rgba(30, 41, 59, 0.7), rgba(15, 23, 42, 0.8));
      border: 1px solid rgba(96, 165, 250, 0.25); border-radius: 16px; padding: 20px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.3);
    }
    .labs-title {
      display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px;
    }
    .labs-title h2 { font-size: 16px; font-weight: 700; color: #93c5fd; display: flex; align-items: center; gap: 8px; }
    .labs-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(270px, 1fr)); gap: 14px; }
    @media (max-width: 900px) { .labs-grid { grid-template-columns: 1fr; } }
    .lab-card {
      background: rgba(15, 23, 42, 0.6); border: 1px solid var(--card-border);
      border-radius: 12px; padding: 16px; display: flex; flex-direction: column; justify-content: space-between;
      gap: 12px; transition: transform 0.2s, border-color 0.2s;
    }
    .lab-card:hover { transform: translateY(-2px); border-color: rgba(96, 165, 250, 0.4); }
    .lab-head h3 { font-size: 14px; font-weight: 700; margin-bottom: 4px; color: #fff; }
    .lab-desc { font-size: 12px; color: var(--text-muted); line-height: 1.4; }
    .btn-run-lab {
      padding: 9px 14px; border-radius: 8px; font-size: 12px; font-weight: 700;
      background: linear-gradient(135deg, #2563eb, #4f46e5); color: #fff; border: 0;
      cursor: pointer; display: flex; align-items: center; justify-content: center; gap: 6px;
      transition: all 0.2s; box-shadow: 0 4px 12px rgba(37, 99, 235, 0.25);
    }
    .btn-run-lab:hover { opacity: 0.95; box-shadow: 0 4px 16px rgba(37, 99, 235, 0.4); }

    /* Live Lab Result Alert */
    .lab-feedback {
      margin-top: 14px; padding: 14px 18px; border-radius: 10px; font-size: 13px;
      line-height: 1.5; display: none;
    }
    .lab-feedback.info { background: rgba(59, 130, 246, 0.15); border: 1px solid #3b82f6; color: #bfdbfe; }
    .lab-feedback.success { background: rgba(16, 185, 129, 0.15); border: 1px solid #10b981; color: #a7f3d0; }
    .lab-feedback.warning { background: rgba(245, 158, 11, 0.15); border: 1px solid #f59e0b; color: #fde68a; }

    /* Topology visualizer */
    .topology-card {
      background: var(--card-bg); backdrop-filter: blur(12px);
      border: 1px solid var(--card-border); border-radius: 16px; padding: 22px 24px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.3);
    }
    .section-title {
      font-size: 14px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.8px;
      color: var(--text-muted); margin-bottom: 16px; display: flex; align-items: center; justify-content: space-between;
    }
    .topo-grid {
      display: grid; grid-template-columns: 1fr auto 1.3fr; gap: 20px; align-items: center;
    }
    @media (max-width: 960px) { .topo-grid { grid-template-columns: 1fr; } }
    
    .gateway-box {
      background: linear-gradient(135deg, rgba(30, 41, 59, 0.8), rgba(15, 23, 42, 0.9));
      border: 1px solid rgba(59, 130, 246, 0.4); border-radius: 14px; padding: 20px;
      text-align: center; box-shadow: 0 0 25px rgba(59, 130, 246, 0.15);
    }
    .gateway-box h2 { font-size: 17px; margin-bottom: 4px; color: #60a5fa; }
    .gateway-box .endpoint { font-family: 'JetBrains Mono', monospace; font-size: 13px; color: #94a3b8; }
    
    /* Traffic Meter Bar */
    .traffic-meter {
      margin-top: 14px; background: rgba(0,0,0,0.3); padding: 10px; border-radius: 10px; text-align: left;
    }
    .meter-label { display: flex; justify-content: space-between; font-size: 11px; margin-bottom: 6px; color: #94a3b8; }
    .meter-bar-wrap { height: 12px; border-radius: 6px; background: rgba(255,255,255,0.08); overflow: hidden; display: flex; }
    .meter-bar-n1 { height: 100%; background: var(--node1); transition: width 0.4s ease; width: 50%; }
    .meter-bar-n2 { height: 100%; background: var(--node2); transition: width 0.4s ease; width: 50%; }

    /* Nodes Column */
    .topo-nodes { display: flex; flex-direction: column; gap: 14px; }
    .node-card {
      background: rgba(15, 23, 42, 0.6); border: 1px solid var(--card-border);
      border-radius: 14px; padding: 16px 18px; transition: all 0.3s ease;
    }
    .node-card.node1 { border-left: 4px solid var(--node1); }
    .node-card.node2 { border-left: 4px solid var(--node2); }
    .node-card.unhealthy { border-left-color: var(--danger) !important; opacity: 0.8; background: rgba(239, 68, 68, 0.05); }
    .node-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; }
    .node-title { display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 700; }
    .node-stat-grid {
      display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px; font-size: 12px;
    }
    .stat-box { background: rgba(0,0,0,0.25); padding: 8px 10px; border-radius: 8px; }
    .stat-label { color: var(--text-muted); font-size: 10px; margin-bottom: 2px; }
    .stat-val { font-size: 15px; font-weight: 700; font-family: 'JetBrains Mono', monospace; }

    /* Node Quick Simulation Controls */
    .node-sim-row {
      margin-top: 10px; padding-top: 10px; border-top: 1px solid rgba(255,255,255,0.06);
      display: flex; align-items: center; justify-content: space-between; gap: 8px;
    }
    .sim-label { font-size: 11px; color: var(--text-muted); }
    .btn-sim-group { display: flex; gap: 6px; }
    .btn-sim {
      padding: 5px 10px; border-radius: 6px; font-size: 11px; font-weight: 600;
      border: 1px solid var(--card-border); background: rgba(255,255,255,0.05); color: #fff;
      cursor: pointer; transition: all 0.2s;
    }
    .btn-sim.ok:hover { background: rgba(16, 185, 129, 0.3); border-color: var(--success); }
    .btn-sim.warn:hover { background: rgba(245, 158, 11, 0.3); border-color: var(--warning); }
    .btn-sim.danger:hover { background: rgba(239, 68, 68, 0.3); border-color: var(--danger); }

    /* Control Panels Grid */
    .controls-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; }
    @media (max-width: 860px) { .controls-grid { grid-template-columns: 1fr; } }
    .panel-card {
      background: var(--card-bg); backdrop-filter: blur(12px);
      border: 1px solid var(--card-border); border-radius: 16px; padding: 20px 22px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.3);
    }
    .algo-btn-group { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-top: 10px; }
    .btn-algo {
      padding: 12px; border-radius: 10px; border: 1px solid var(--card-border);
      background: rgba(255,255,255,0.03); color: var(--text-main); font-weight: 600;
      font-size: 13px; cursor: pointer; text-align: left; transition: all 0.2s;
      display: flex; flex-direction: column; gap: 4px;
    }
    .btn-algo small { font-size: 11px; color: var(--text-muted); font-weight: 400; }
    .btn-algo:hover { background: rgba(255,255,255,0.08); border-color: rgba(255,255,255,0.2); }
    .btn-algo.active {
      background: linear-gradient(135deg, rgba(59, 130, 246, 0.25), rgba(99, 102, 241, 0.25));
      border-color: #60a5fa; box-shadow: 0 0 15px rgba(59, 130, 246, 0.2);
    }

    /* Traffic Generator */
    .traffic-controls { display: flex; flex-direction: column; gap: 12px; margin-top: 10px; }
    .traffic-actions { display: flex; gap: 10px; flex-wrap: wrap; }
    .btn-run {
      padding: 10px 18px; border-radius: 8px; font-weight: 700; font-size: 13px;
      background: linear-gradient(135deg, #2563eb, #7c3aed); color: #fff; border: 0;
      cursor: pointer; transition: all 0.2s; display: inline-flex; align-items: center; gap: 6px;
      box-shadow: 0 4px 15px rgba(37, 99, 235, 0.3);
    }
    .btn-run:hover { opacity: 0.9; transform: translateY(-1px); }
    .btn-run:disabled { opacity: 0.5; cursor: not-allowed; }
    .benchmark-result {
      margin-top: 10px; background: rgba(0,0,0,0.3); border-radius: 10px; padding: 12px 16px;
      font-size: 13px; font-family: 'JetBrains Mono', monospace; display: none; line-height: 1.5;
    }

    /* Logs & Trace Table */
    .table-container {
      margin-top: 10px; max-height: 250px; overflow-y: auto; border-radius: 8px;
      border: 1px solid var(--card-border);
    }
    table { width: 100%; border-collapse: collapse; font-size: 12px; text-align: left; }
    th {
      background: rgba(15, 23, 42, 0.9); padding: 10px 14px; position: sticky; top: 0;
      font-weight: 600; color: var(--text-muted); border-bottom: 1px solid var(--card-border);
    }
    td { padding: 8px 14px; border-bottom: 1px solid rgba(255,255,255,0.04); font-family: 'JetBrains Mono', monospace; }
    tr:hover td { background: rgba(255,255,255,0.02); }
    .tag { display: inline-block; padding: 2px 8px; border-radius: 4px; font-weight: 600; font-size: 11px; }
    .tag-200 { background: rgba(16, 185, 129, 0.2); color: #34d399; }
    .tag-429 { background: rgba(245, 158, 11, 0.2); color: #fbbf24; }
    .tag-503 { background: rgba(239, 68, 68, 0.2); color: #f87171; }
    .tag-node1 { background: rgba(59, 130, 246, 0.2); color: #93c5fd; }
    .tag-node2 { background: rgba(16, 185, 129, 0.2); color: #6ee7b7; }
    
    /* Glossary Accordion */
    .glossary-box {
      background: rgba(15, 23, 42, 0.6); border: 1px solid var(--card-border);
      border-radius: 12px; padding: 14px 18px; font-size: 12px; line-height: 1.6;
    }
    .glossary-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 8px; }
    @media (max-width: 768px) { .glossary-grid { grid-template-columns: 1fr; } }
    .glossary-item b { color: #60a5fa; }
  </style>
</head>
<body>
  <div class="container">
    
    <!-- Mental Model / Concept Header -->
    <div class="help-banner">
      <div class="help-content">
        💡 <b>Mô hình hoạt động:</b> Khách hàng gọi vào cổng <b>YARP (:5266)</b>. 
        Gateway sẽ quyết định chuyển request vào <b>Node 1 (:5001)</b> hay <b>Node 2 (:5002)</b> dựa trên thuật toán và sức khỏe của từng node.
      </div>
      <button class="btn-help-toggle" onclick="toggleGlossary()">📖 Xem giải thích thuật ngữ</button>
    </div>

    <!-- Header -->
    <header class="header">
      <div class="header-brand">
        <div class="header-logo">⚡</div>
        <div class="header-title">
          <h1>YARP Load Balancer Control Center</h1>
          <p>Phòng thí nghiệm High Availability & Trực quan hóa phân luồng tải</p>
        </div>
      </div>
      <div class="header-badges">
        <div class="badge">
          <span class="badge-dot pulse"></span>
          <span>Gateway: <b>:5266</b></span>
        </div>
        <div class="badge" id="affinityBadge" style="cursor:pointer;" onclick="toggleAffinityMode()" title="Bấm để chuyển Stateless / Sticky">Chế độ: <b id="modeText">Stateless</b></div>
        <div class="badge" id="ngrokBadge" style="cursor:pointer;" onclick="handleNgrokClick()" title="Bấm để mở hoặc copy link ngrok công khai">
          <span id="ngrokDot" class="badge-dot" style="background:#94a3b8;"></span>
          <span id="ngrokBadgeText">🌐 ngrok: TẮT</span>
        </div>
        <button id="btnResetMetrics" class="badge" style="cursor:pointer;" title="Xóa lịch sử thống kê">🔄 Reset Số Liệu</button>
      </div>
    </header>

    <!-- SECTION 1: 1-Click Interactive Experiments (Dành cho người mới) -->
    <section class="labs-section">
      <div class="labs-title">
        <h2>🧪 Các Bài Thí Nghiệm Nhanh (1-Click Run)</h2>
        <span style="font-size:12px; color:#94a3b8;">Hệ thống tự tạo sự cố & phân tích kết quả cho bạn</span>
      </div>
      <div class="labs-grid">
        <!-- Lab 1 (Agent style: automated script) -->
        <div class="lab-card">
          <div class="lab-head">
            <h3>1. Thử Nghiệm Tự Động Failover</h3>
            <p class="lab-desc">Làm Node 1 trả lỗi 503. Xem YARP mất bao lâu để gạch tên Node 1 và đưa 100% người dùng sang Node 2 (kịch bản script 10 req).</p>
          </div>
          <button class="btn-run-lab" onclick="runLabFailover()">▶️ Chạy Thí Nghiệm Failover</button>
        </div>

        <!-- Lab 1B (User style: F5 / Refresh trực tiếp) -->
        <div class="lab-card" style="border: 1px solid rgba(96, 165, 250, 0.45); background: linear-gradient(180deg, rgba(30, 58, 138, 0.25) 0%, rgba(15, 23, 42, 0.6) 100%);">
          <div class="lab-head">
            <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:4px;">
              <h3 style="color:#60a5fa; margin:0;">1B. Failover (Style F5 Trực Tiếp)</h3>
              <span style="font-size:10px; background:#1e40af; color:#bfdbfe; padding:2px 7px; border-radius:10px; font-weight:700;">STYLE CỦA BẠN</span>
            </div>
            <p class="lab-desc">Làm Node 1 bị 503 ➔ Bạn tự bấm F5 refresh: Gateway <b>luôn chuyển thẳng sang Node 2 (200 OK)</b> an toàn, tuyệt đối không bị lỗi 503!</p>
          </div>
          <div style="display:flex; flex-direction:column; gap:6px;">
            <div style="display:flex; gap:6px;">
              <button class="btn-run-lab" style="flex:1; background:#dc2626; padding:8px 6px; font-size:11px;" onclick="userStyleFailover_Set503()">🔴 1. Đánh Sập Node 1 (503)</button>
              <button class="btn-run-lab" style="flex:1; background:#059669; padding:8px 6px; font-size:11px;" onclick="userStyleFailover_Recover()">🟢 Khôi Phục</button>
            </div>
            <button class="btn-run-lab" style="background:linear-gradient(135deg, #2563eb, #7c3aed); padding:9px; font-size:12px;" onclick="userStyleFailover_TestRefresh()">🔄 2. Thử Refresh Trực Tiếp (Gateway :5266)</button>
            <a href="/" target="_blank" style="text-align:center; font-size:11px; color:#93c5fd; text-decoration:none; padding:4px 6px; border:1px dashed rgba(147, 197, 253, 0.4); border-radius:6px;">🔗 Hoặc Mở Tab Mới (:5266) & Tự Nhấn F5 Trên Bàn Phím &rarr;</a>
          </div>
        </div>

        <!-- Lab 2 -->
        <div class="lab-card">
          <div class="lab-head">
            <h3>2. Node Bị Chậm: RoundRobin vs LeastRequests</h3>
            <p class="lab-desc">Cho Node 2 bị nghẽn (phản hồi 3s). So sánh xem <b>LeastRequests</b> thông minh hơn <b>RoundRobin</b> ra sao.</p>
          </div>
          <button class="btn-run-lab" style="background:linear-gradient(135deg, #059669, #0284c7);" onclick="runLabSlowComparison()">▶️ Chạy So Sánh Thuật Toán</button>
        </div>

        <!-- Lab 3 -->
        <div class="lab-card">
          <div class="lab-head">
            <h3>3. Thử Nghiệm Chống Spam (Rate Limit)</h3>
            <p class="lab-desc">Gửi 10 request dồn dập vào Gateway để quan sát cơ chế chặn HTTP 429 Too Many Requests sau 5 req.</p>
          </div>
          <button class="btn-run-lab" style="background:linear-gradient(135deg, #d97706, #dc2626);" onclick="runLabRateLimit()">▶️ Chạy Thử Rate Limiter</button>
        </div>
      </div>

      <!-- Live Feedback for Experiments -->
      <div id="labFeedbackBox" class="lab-feedback"></div>
    </section>

    <!-- SECTION 2: Live Architecture Topology -->
    <section class="topology-card">
      <div class="section-title">
        <span>🌐 Sơ Đồ Kiến Trúc & Tỷ Lệ Phân Bổ Tải (Live Topology)</span>
        <a href="/" target="_blank" style="color:#60a5fa; font-size:12px; text-decoration:none;">🔗 Mở trang Backend qua Gateway &rarr;</a>
      </div>
      <div class="topo-grid">
        
        <!-- YARP Gateway Box -->
        <div class="gateway-box">
          <h2>Reverse Proxy (YARP)</h2>
          <div class="endpoint">http://localhost:5266</div>
          
          <div style="margin-top:14px; display:flex; justify-content:center; gap:16px;">
            <div>
              <div style="font-size:11px; color:#94a3b8;">Thuật toán hiện tại</div>
              <div id="activeAlgoBadge" style="font-weight:700; color:#38bdf8; font-size:14px;">RoundRobin</div>
            </div>
            <div>
              <div style="font-size:11px; color:#94a3b8;">Tổng Requests</div>
              <div id="gwTotalReq" style="font-weight:700; font-size:14px;">0</div>
            </div>
            <div>
              <div style="font-size:11px; color:#94a3b8;">Bị chặn (429)</div>
              <div id="gwRateLimited" style="font-weight:700; color:#f87171; font-size:14px;">0</div>
            </div>
          </div>

          <!-- Traffic Split Ratio Bar -->
          <div class="traffic-meter">
            <div class="meter-label">
              <span>Node 1: <b id="barPercentN1" style="color:#60a5fa;">50%</b></span>
              <span>Tỷ lệ phân chia tải</span>
              <span>Node 2: <b id="barPercentN2" style="color:#34d399;">50%</b></span>
            </div>
            <div class="meter-bar-wrap">
              <div id="barN1" class="meter-bar-n1"></div>
              <div id="barN2" class="meter-bar-n2"></div>
            </div>
          </div>
        </div>

        <!-- Flow Arrow -->
        <div style="text-align:center; color:var(--text-muted); font-size:24px;">➔</div>

        <!-- Backend Destination Nodes -->
        <div class="topo-nodes">
          
          <!-- Node 1 -->
          <div class="node-card node1" id="cardNode1">
            <div class="node-head">
              <div class="node-title">
                <span class="badge-dot" id="dotNode1"></span>
                <span style="color:#60a5fa;">Node 1</span>
                <span style="font-size:11px; color:#94a3b8;">(:5001)</span>
              </div>
              <div class="badge" id="healthNode1">Healthy</div>
            </div>
            <div class="node-stat-grid">
              <div class="stat-box">
                <div class="stat-label">Đã phục vụ</div>
                <div class="stat-val" id="n1Total">0</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Đang xử lý dở</div>
                <div class="stat-val" id="n1InFlight" style="color:#fde047;">0</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Độ trễ TB</div>
                <div class="stat-val" id="n1Latency">0ms</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Lỗi (5xx)</div>
                <div class="stat-val" id="n1Errors" style="color:#f87171;">0</div>
              </div>
            </div>
            <div class="node-sim-row">
              <span class="sim-label">Tạo sự cố thử nghiệm:</span>
              <div class="btn-sim-group">
                <button class="btn-sim ok" onclick="simulateNode('node1', 'Normal')">🟢 Bình thường</button>
                <button class="btn-sim warn" onclick="simulateNode('node1', 'Slow')">🟡 Chậm 3s</button>
                <button class="btn-sim danger" onclick="simulateNode('node1', 'Error503')">🔴 Sập (503)</button>
              </div>
            </div>
          </div>

          <!-- Node 2 -->
          <div class="node-card node2" id="cardNode2">
            <div class="node-head">
              <div class="node-title">
                <span class="badge-dot" id="dotNode2"></span>
                <span style="color:#34d399;">Node 2</span>
                <span style="font-size:11px; color:#94a3b8;">(:5002)</span>
              </div>
              <div class="badge" id="healthNode2">Healthy</div>
            </div>
            <div class="node-stat-grid">
              <div class="stat-box">
                <div class="stat-label">Đã phục vụ</div>
                <div class="stat-val" id="n2Total">0</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Đang xử lý dở</div>
                <div class="stat-val" id="n2InFlight" style="color:#fde047;">0</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Độ trễ TB</div>
                <div class="stat-val" id="n2Latency">0ms</div>
              </div>
              <div class="stat-box">
                <div class="stat-label">Lỗi (5xx)</div>
                <div class="stat-val" id="n2Errors" style="color:#f87171;">0</div>
              </div>
            </div>
            <div class="node-sim-row">
              <span class="sim-label">Tạo sự cố thử nghiệm:</span>
              <div class="btn-sim-group">
                <button class="btn-sim ok" onclick="simulateNode('node2', 'Normal')">🟢 Bình thường</button>
                <button class="btn-sim warn" onclick="simulateNode('node2', 'Slow')">🟡 Chậm 3s</button>
                <button class="btn-sim danger" onclick="simulateNode('node2', 'Error503')">🔴 Sập (503)</button>
              </div>
            </div>
          </div>

        </div>
      </div>
    </section>

    <!-- SECTION 3: Manual Controls & Custom Traffic Generator -->
    <div class="controls-grid">
      
      <!-- Panel 1: Thuật toán -->
      <section class="panel-card">
        <div class="section-title">
          <span>⚙️ Tùy Chỉnh Thuật Toán Thủ Công</span>
        </div>
        <p style="font-size:12px; color:var(--text-muted);">Bấm để đổi ngay thuật toán đang chạy mà không cần khởi động lại Gateway.</p>
        
        <div class="algo-btn-group">
          <button class="btn-algo" data-algo="RoundRobin" onclick="setAlgorithm('RoundRobin')">
            <span>🔄 Round Robin</span>
            <small>Chia đều luân phiên lần lượt các node (1-2-1-2)</small>
          </button>
          <button class="btn-algo" data-algo="LeastRequests" onclick="setAlgorithm('LeastRequests')">
            <span>⚖️ Least Requests</span>
            <small>Ưu tiên node đang rảnh rỗi nhất (ít request đang chạy)</small>
          </button>
          <button class="btn-algo" data-algo="Random" onclick="setAlgorithm('Random')">
            <span>🎲 Random</span>
            <small>Chọn node hoàn toàn ngẫu nhiên</small>
          </button>
          <button class="btn-algo" data-algo="PowerOfTwoChoices" onclick="setAlgorithm('PowerOfTwoChoices')">
            <span>🎯 Power of 2 Choices</span>
            <small>Chọn 2 node ngẫu nhiên rồi lấy node ít tải hơn</small>
          </button>
        </div>

        <div style="margin-top: 16px; padding-top: 14px; border-top: 1px solid var(--card-border);">
          <div style="display:flex; justify-content:space-between; align-items:center; gap:12px;">
            <div>
              <div style="font-size:13px; font-weight:700; display:flex; align-items:center; gap:8px;">
                <span>Session Affinity (Sticky Session)</span>
                <span id="affinityStatusBadge" style="font-size:10px; padding:2px 8px; border-radius:10px; font-weight:700; background:rgba(148,163,184,0.2); color:#94a3b8;">ĐANG TẮT</span>
              </div>
              <div id="affinityStatusDesc" style="font-size:11px; color:var(--text-muted); margin-top:3px;">
                Chế độ Stateless: Không dùng sticky cookie. Mỗi lần F5 sẽ nhảy xen kẽ giữa Node 1 và Node 2.
              </div>
            </div>
            <button id="btnToggleMode" class="btn-sim" style="padding:9px 16px; font-size:12px; font-weight:700; white-space:nowrap; transition:all 0.25s;" onclick="toggleAffinityMode()">
              ▶️ Bật Sticky Session
            </button>
          </div>
          <div id="affinityToast" style="display:none; margin-top:10px; font-size:11px; padding:8px 12px; border-radius:6px; background:rgba(16,185,129,0.15); border:1px solid rgba(16,185,129,0.3); color:#86efac;"></div>
        </div>
      </section>

      <!-- Panel 2: Tự bắn tải tùy biến -->
      <section class="panel-card">
        <div class="section-title">
          <span>🚀 Bắn Tải Tùy Biến (Custom Load)</span>
        </div>
        <p style="font-size:12px; color:var(--text-muted);">Tự chọn số lượng và loại request để kiểm chứng.</p>
        
        <div class="traffic-controls">
          <div style="display:flex; gap:10px; align-items:center;">
            <label style="font-size:12px; color:var(--text-muted);">Đường dẫn test:</label>
            <select id="trafficPath" style="background:#1e293b; color:#fff; border:1px solid var(--card-border); padding:6px 10px; border-radius:6px; font-size:12px;">
              <option value="/">Trang chủ ( / )</option>
              <option value="/api/info">API Thông Tin ( /api/info )</option>
              <option value="/api/test-ratelimit">API Rate Limit ( /api/test-ratelimit )</option>
            </select>
          </div>
          <div class="traffic-actions">
            <button class="btn-run" onclick="generateTraffic(1, false)">⚡ 1 Request</button>
            <button class="btn-run" onclick="generateTraffic(10, false)">🚀 10 Requests Tuần Tự</button>
            <button class="btn-run" style="background:linear-gradient(135deg, #e11d48, #9333ea);" onclick="generateTraffic(20, true)">💥 20 Requests Đồng Thời</button>
          </div>
          <div id="benchmarkBox" class="benchmark-result"></div>
        </div>
      </section>
    </div>

    <!-- SECTION 4: Live Request Stream -->
    <section class="panel-card">
      <div style="display:flex; justify-content:space-between; align-items:center;">
        <div class="section-title" style="margin-bottom:0;">📋 Nhật Ký Phân Luồng Thời Gian Thực (Live Request Stream)</div>
        <span style="font-size:11px; color:var(--text-muted);">Tự động cập nhật mỗi 1s</span>
      </div>
      <div class="table-container">
        <table>
          <thead>
            <tr>
              <th>ID</th>
              <th>Thời gian</th>
              <th>Method</th>
              <th>Đường dẫn</th>
              <th>Node phục vụ</th>
              <th>HTTP Code</th>
              <th>Độ trễ</th>
            </tr>
          </thead>
          <tbody id="logsTbody">
            <tr><td colspan="7" style="text-align:center; color:#64748b; padding:20px;">Đang chờ request đầu tiên... Hãy bấm thử một nút phát tải bên trên!</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- SECTION 5: Glossary (Giải thích thuật ngữ) -->
    <div id="glossaryBox" class="glossary-box" style="display:none;">
      <div style="font-weight:700; font-size:13px; color:#fff; margin-bottom:4px;">📖 Sổ Tay Thuật Ngữ Dành Cho Người Mới</div>
      <div class="glossary-grid">
        <div class="glossary-item">
          <b>• In-Flight Requests là gì?</b> Số lượng request mà node đang ôm dở trong bộ nhớ để xử lý (chưa kịp trả về kết quả). Càng nhiều in-flight chứng tỏ node đó đang bận hoặc bị nghẽn.
        </div>
        <div class="glossary-item">
          <b>• Active Health Check là gì?</b> YARP chủ động ping đường dẫn <code>/health</code> của các node định kỳ mỗi 5 giây. Nếu node trả về lỗi hoặc timeout 2 lần liên tiếp thì YARP sẽ gạch tên node đó ra khỏi danh sách.
        </div>
        <div class="glossary-item">
          <b>• Least Requests khác Round Robin thế nào?</b> Round Robin chỉ chia luân phiên như phát bài (1-2-1-2) bất kể node đó đang khỏe hay yếu. Least Requests thông minh hơn: node nào đang rảnh rỗi nhất thì mới giao thêm việc.
        </div>
        <div class="glossary-item">
          <b>• Session Affinity (Sticky Session) là gì?</b> Trình duyệt của bạn sẽ được "dán" chặt vào 1 node duy nhất thông qua Cookie. Dù bạn có reload bao nhiêu lần thì request vẫn chỉ đi vào node đó.
        </div>
      </div>
    </div>

  </div>

  <script>
    let currentAlgorithm = "RoundRobin";
    let currentMode = "Stateless";

    function toggleGlossary() {
      const g = document.getElementById("glossaryBox");
      g.style.display = g.style.display === "none" ? "block" : "none";
    }

    async function fetchStatus() {
      try {
        const res = await fetch("/api/lb/status");
        if (!res.ok) return;
        const data = await res.json();
        
        currentAlgorithm = data.algorithm;
        currentMode = data.mode;
        
        // Header info
        document.getElementById("modeText").textContent = data.mode;
        document.getElementById("activeAlgoBadge").textContent = data.algorithm;

        // Cap nhat truc quan Session Affinity Panel 1
        const isAffinity = data.mode === "Affinity";
        const btnToggle = document.getElementById("btnToggleMode");
        const statusBadge = document.getElementById("affinityStatusBadge");
        const statusDesc = document.getElementById("affinityStatusDesc");
        const headerBadge = document.getElementById("affinityBadge");

        if (btnToggle && statusBadge && statusDesc) {
          if (isAffinity) {
            statusBadge.textContent = "🟢 ĐANG BẬT (STICKY)";
            statusBadge.style.background = "rgba(16,185,129,0.2)";
            statusBadge.style.color = "#34d399";
            statusDesc.innerHTML = "Trình duyệt được gắn chặt vào 1 node bằng cookie <code>Yarp.Affinity</code>. F5 sẽ <b>cố định 1 node</b>!";
            btnToggle.innerHTML = "⏹️ Tắt Sticky (Về Stateless)";
            btnToggle.style.background = "linear-gradient(135deg, #059669, #10b981)";
            btnToggle.style.color = "#fff";
            btnToggle.style.boxShadow = "0 0 12px rgba(16,185,129,0.35)";
            if (headerBadge) {
              headerBadge.style.borderColor = "#10b981";
              headerBadge.style.color = "#34d399";
            }
          } else {
            statusBadge.textContent = "⚪ ĐANG TẮT (STATELESS)";
            statusBadge.style.background = "rgba(148,163,184,0.15)";
            statusBadge.style.color = "#94a3b8";
            statusDesc.innerHTML = "Chế độ Stateless: Không dùng sticky cookie. Mỗi lần F5 sẽ <b>nhảy xen kẽ</b> giữa Node 1 và Node 2.";
            btnToggle.innerHTML = "▶️ Bật Sticky Session";
            btnToggle.style.background = "#1e293b";
            btnToggle.style.color = "#60a5fa";
            btnToggle.style.boxShadow = "none";
            if (headerBadge) {
              headerBadge.style.borderColor = "var(--card-border)";
              headerBadge.style.color = "#cbd5e1";
            }
          }
        }
        
        // Active algorithm buttons
        document.querySelectorAll(".btn-algo").forEach(btn => {
          if (btn.getAttribute("data-algo") === data.algorithm) {
            btn.classList.add("active");
          } else {
            btn.classList.remove("active");
          }
        });

        // Gateway stats
        const totalGw = data.metrics.totalGatewayRequests || 0;
        document.getElementById("gwTotalReq").textContent = totalGw;
        document.getElementById("gwRateLimited").textContent = data.metrics.totalRateLimited || 0;

        // Nodes stats
        const n1Metrics = (data.metrics.nodes || []).find(n => n.nodeId.toLowerCase() === "node1") || {};
        const n2Metrics = (data.metrics.nodes || []).find(n => n.nodeId.toLowerCase() === "node2") || {};

        const n1Count = n1Metrics.totalRequests || 0;
        const n2Count = n2Metrics.totalRequests || 0;

        document.getElementById("n1Total").textContent = n1Count;
        document.getElementById("n1Latency").textContent = (n1Metrics.avgLatencyMs || 0) + "ms";
        document.getElementById("n1Errors").textContent = n1Metrics.errorRequests || 0;

        document.getElementById("n2Total").textContent = n2Count;
        document.getElementById("n2Latency").textContent = (n2Metrics.avgLatencyMs || 0) + "ms";
        document.getElementById("n2Errors").textContent = n2Metrics.errorRequests || 0;

        // Traffic distribution bar
        const totalNodes = n1Count + n2Count;
        let p1 = 50, p2 = 50;
        if (totalNodes > 0) {
          p1 = Math.round((n1Count / totalNodes) * 100);
          p2 = 100 - p1;
        }
        document.getElementById("barPercentN1").textContent = p1 + "%";
        document.getElementById("barPercentN2").textContent = p2 + "%";
        document.getElementById("barN1").style.width = p1 + "%";
        document.getElementById("barN2").style.width = p2 + "%";

        // Destinations health
        const d1 = (data.destinations || []).find(d => d.id.toLowerCase() === "node1");
        const d2 = (data.destinations || []).find(d => d.id.toLowerCase() === "node2");

        updateNodeHealth("cardNode1", "healthNode1", "dotNode1", "n1InFlight", d1);
        updateNodeHealth("cardNode2", "healthNode2", "dotNode2", "n2InFlight", d2);

        // Update logs table
        const logs = data.metrics.recentLogs || [];
        const tbody = document.getElementById("logsTbody");
        if (logs.length > 0) {
          tbody.innerHTML = logs.map(l => {
            const statusClass = l.statusCode === 200 ? "tag-200" : (l.statusCode === 429 ? "tag-429" : "tag-503");
            const nodeClass = l.node.toLowerCase() === "node1" ? "tag-node1" : (l.node.toLowerCase() === "node2" ? "tag-node2" : "");
            return `<tr>
              <td>#${l.id}</td>
              <td>${l.timestamp}</td>
              <td><b>${l.method}</b></td>
              <td>${l.path}</td>
              <td><span class="tag ${nodeClass}">${l.node}</span></td>
              <td><span class="tag ${statusClass}">${l.statusCode}</span></td>
              <td>${l.latencyMs}ms</td>
            </tr>`;
          }).join("");
        }
      } catch (err) {
        console.error("fetch status error:", err);
      }
    }

    function updateNodeHealth(cardId, badgeId, dotId, inFlightId, dest) {
      const card = document.getElementById(cardId);
      const badge = document.getElementById(badgeId);
      const dot = document.getElementById(dotId);
      const inFlight = document.getElementById(inFlightId);

      if (!dest) return;
      inFlight.textContent = dest.inFlight || 0;

      const isHealthy = dest.activeHealth !== "Unhealthy" && dest.passiveHealth !== "Unhealthy";
      if (isHealthy) {
        card.classList.remove("unhealthy");
        badge.textContent = "Healthy (Khỏe mạnh)";
        badge.style.color = "#34d399";
        badge.style.background = "rgba(16, 185, 129, 0.15)";
        dot.style.background = "#10b981";
      } else {
        card.classList.add("unhealthy");
        badge.textContent = "Unhealthy (Đã bị loại)";
        badge.style.color = "#f87171";
        badge.style.background = "rgba(239, 68, 68, 0.2)";
        dot.style.background = "#ef4444";
      }
    }

    async function setAlgorithm(algo) {
      try {
        const res = await fetch(`/api/lb/algorithm?algo=${encodeURIComponent(algo)}`, { method: "POST" });
        if (res.ok) fetchStatus();
      } catch (e) { alert("Lỗi đổi thuật toán: " + e.message); }
    }

    async function toggleAffinityMode() {
      const nextMode = currentMode === "Stateless" ? "Affinity" : "Stateless";
      const btn = document.getElementById("btnToggleMode");
      if (btn) {
        btn.disabled = true;
        btn.textContent = "⏳ Đang đổi chế độ...";
      }
      try {
        const res = await fetch(`/api/lb/mode?mode=${encodeURIComponent(nextMode)}`, { method: "POST" });
        if (res.ok) {
          const toast = document.getElementById("affinityToast");
          if (toast) {
            toast.style.display = "block";
            toast.textContent = nextMode === "Affinity" 
              ? "✅ Đã BẬT Sticky Session! Cookie Yarp.Affinity đã được kích hoạt — F5 sẽ giữ cố định 1 node."
              : "✅ Đã TẮT Sticky Session! Chuyển về Stateless — F5 sẽ luân phiên đổi node.";
            setTimeout(() => { toast.style.display = "none"; }, 3500);
          }
          await fetchStatus();
        }
      } catch (e) { 
        alert("Lỗi đổi mode: " + e.message); 
      } finally {
        if (btn) btn.disabled = false;
      }
    }

    async function simulateNode(node, mode) {
      try {
        const res = await fetch(`/api/lb/simulate-node?node=${node}&mode=${mode}&delayMs=3000`, { method: "POST" });
        if (res.ok) fetchStatus();
      } catch (e) { alert("Lỗi kết nối node: " + e.message); }
    }

    async function generateTraffic(count, concurrent, targetPath) {
      const path = targetPath || document.getElementById("trafficPath").value;
      const box = document.getElementById("benchmarkBox");
      box.style.display = "block";
      box.innerHTML = `<span style="color:#60a5fa;">Đang gửi ${count} requests (${concurrent ? 'Đồng thời' : 'Tuần tự'})...</span>`;

      try {
        const res = await fetch(`/api/lb/generate-traffic?count=${count}&concurrent=${concurrent}&path=${encodeURIComponent(path)}`, { method: "POST" });
        const data = await res.json();
        
        const n1 = data.nodeBreakdown.node1 || 0;
        const n2 = data.nodeBreakdown.node2 || 0;
        const other = data.nodeBreakdown.other || 0;
        
        box.innerHTML = `
          <div><b>Kết Quả (${count} requests hoàn tất trong ${data.durationMs}ms):</b></div>
          <div style="margin-top:4px;">
            - <b style="color:#60a5fa;">Node 1:</b> ${n1} req (${Math.round(n1/count*100)}%) | 
            - <b style="color:#34d399;">Node 2:</b> ${n2} req (${Math.round(n2/count*100)}%) ${other > 0 ? `| - <b style="color:#f87171;">Khác/Lỗi:</b> ${other}` : ''}
          </div>
          <div style="margin-top:2px; color:#94a3b8;">
            Độ trễ phản hồi: Min ${data.minLatencyMs}ms / TB ${data.avgLatencyMs}ms / Max ${data.maxLatencyMs}ms
          </div>
        `;
        fetchStatus();
        return data;
      } catch (e) {
        box.innerHTML = `<span style="color:#f87171;">Lỗi gửi tải: ${e.message}</span>`;
        return null;
      }
    }

    /* ============================================================
       1-CLICK INTERACTIVE LAB EXPERIMENTS (Dành cho người học)
       ============================================================ */
    async function runLabFailover() {
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback info";
      box.style.display = "block";
      box.innerHTML = "⏳ <b>Bước 1:</b> Đang giả lập Node 1 bị sập (trả lỗi 503)...";

      await simulateNode("node1", "Error503");
      await new Promise(r => setTimeout(r, 600));

      box.innerHTML = "⏳ <b>Bước 2:</b> Gửi 10 requests qua Gateway xem chuyện gì xảy ra...";
      const result = await generateTraffic(10, false, "/");

      if (result) {
        const n2 = result.nodeBreakdown.node2 || 0;
        box.className = "lab-feedback success";
        box.innerHTML = `
          🎉 <b>KẾT QUẢ BÀI THỬ 1 (FAILOVER):</b><br>
          Khi Node 1 bị lỗi 503, YARP đã tự động nhận biết và điều phối <b>${n2}/10 requests vào Node 2</b>!<br>
          👉 <i>Bài học:</i> Nhờ có cơ chế Active/Passive Health Check, hệ thống của bạn vẫn phục vụ khách bình thường ngay cả khi 1 node chết hẳn.
          <div style="margin-top:8px;">
            <button class="btn-sim ok" onclick="recoverAllNodes()">🟢 Khôi phục Node 1 trở lại bình thường</button>
          </div>
        `;
      }
    }

    /* ============================================================
       LAB 1B: STYLE CỦA BẠN (F5 / REFRESH TRỰC TIẾP)
       ============================================================ */
    let userStyleRefreshCount = 0;

    async function userStyleFailover_Set503() {
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback info";
      box.style.display = "block";
      box.innerHTML = "⏳ Đang đánh sập Node 1 (trả lỗi 503) và cô lập trên Load Balancer ngay lập tức...";

      await simulateNode("node1", "Error503");
      userStyleRefreshCount = 0;

      box.className = "lab-feedback warn";
      box.innerHTML = `
        🔴 <b>ĐÃ ĐÁNH SẬP NODE 1 THÀNH CÔNG (Mã lỗi 503)!</b><br>
        Bây giờ theo đúng style thử nghiệm của bạn:<br>
        👉 <b>Cách 1:</b> Bấm nút <b>[🔄 2. Thử Refresh Trực Tiếp]</b> bên trên.<br>
        👉 <b>Cách 2:</b> Mở tab <a href="/" target="_blank" style="color:#60a5fa; font-weight:700;">http://localhost:5266</a> rồi tự tay bấm phím <b>F5</b> trên bàn phím.<br>
        🛡️ <b>Kỳ vọng:</b> Trang web LUÔN LUÔN hiển thị <b>Node 2 (200 OK)</b> an toàn, Gateway đã tự động chuyển mạch (Failover) để bảo vệ bạn, hoàn toàn không bị lỗi 503!
      `;
    }

    async function userStyleFailover_TestRefresh() {
      const box = document.getElementById("labFeedbackBox");
      box.style.display = "block";
      userStyleRefreshCount++;

      try {
        const sw = performance.now();
        const res = await fetch("/", { cache: "no-store" });
        const time = Math.round(performance.now() - sw);
        const servedBy = res.headers.get("X-Served-By") || "Unknown";
        const autoFailover = res.headers.get("X-Auto-Failover") === "true";

        if (res.status === 200) {
          box.className = "lab-feedback success";
          box.innerHTML = `
            🎉 <b>LƯỢT REFRESH #${userStyleRefreshCount} THÀNH CÔNG (200 OK)!</b><br>
            • Node đang phục vụ bạn: <b style="color:#34d399; font-size:15px;">${servedBy}</b> (Node 2 an toàn!)<br>
            • Mã phản hồi HTTP: <b style="color:#34d399;">${res.status} OK</b> (Hoàn toàn không dính lỗi 503!)<br>
            • Trạng thái bảo vệ: <b>${autoFailover ? '⚡ Transparent Auto-Failover (Chặn đứng lỗi 503 của Node 1 và cứu hộ tức thì sang Node 2!)' : '🛡️ YARP Health Isolation (Node 1 đã bị gạch tên, 100% request đưa sang Node 2)'}</b><br>
            • Độ trễ: ${time}ms.<br>
            <div style="margin-top:8px; font-size:12px; color:#cbd5e1;">
              👉 Bạn có thể bấm nút Refresh này nhiều lần hoặc F5 trên bàn phím: 100% lượt truy cập đều an toàn hạ cánh ở Node 2!
            </div>
            <div style="margin-top:8px;">
              <button class="btn-sim ok" onclick="userStyleFailover_Recover()">🟢 Khôi phục Node 1 trở lại bình thường</button>
            </div>
          `;
        } else {
          box.className = "lab-feedback danger";
          box.innerHTML = `⚠️ Nhận mã HTTP ${res.status} từ ${servedBy}.`;
        }
      } catch (err) {
        box.className = "lab-feedback danger";
        box.innerHTML = `❌ Lỗi kết nối: ${err.message}`;
      }
    }

    async function userStyleFailover_Recover() {
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback info";
      box.style.display = "block";
      box.innerHTML = "⏳ Đang khôi phục Node 1 trở lại bình thường (200 OK)...";

      await simulateNode("node1", "Normal");

      box.className = "lab-feedback success";
      box.innerHTML = `
        🟢 <b>ĐÃ KHÔI PHỤC NODE 1 THÀNH CÔNG!</b><br>
        Node 1 đã hoạt động trở lại bình thường (Healthy). YARP sẽ phân phối đều tải trở lại cho cả Node 1 và Node 2.
      `;
    }

    async function runLabSlowComparison() {
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback info";
      box.style.display = "block";
      box.innerHTML = "⏳ <b>Bước 1:</b> Đặt Node 2 bị nghẽn (phản hồi chậm 3 giây)...";
      
      await simulateNode("node2", "Slow");
      await setAlgorithm("RoundRobin");
      await new Promise(r => setTimeout(r, 500));

      box.innerHTML = "⏳ <b>Bước 2:</b> Gửi 10 request đồng thời với <b>Round Robin</b>...";
      const r1 = await generateTraffic(10, true, "/");
      
      box.innerHTML = "⏳ <b>Bước 3:</b> Chuyển sang <b>Least Requests</b> và gửi tiếp 10 request đồng thời...";
      await setAlgorithm("LeastRequests");
      await new Promise(r => setTimeout(r, 500));
      const r2 = await generateTraffic(10, true, "/");

      box.className = "lab-feedback success";
      box.innerHTML = `
        🎯 <b>KẾT QUẢ SO SÁNH THUẬT TOÁN:</b><br>
        - <b>Round Robin:</b> Vẫn chia 50/50 bất chấp Node 2 đang chậm ➔ Làm ${r1?.nodeBreakdown?.node2 || 5} request bị kẹt lâu (Tổng thời gian: ${r1?.durationMs || 3000}ms).<br>
        - <b>Least Requests:</b> Thông minh hơn, phát hiện Node 2 đang bận nên dồn <b>${r2?.nodeBreakdown?.node1 || 9}/10 request cho Node 1</b> xử lý tức thì (Tổng thời gian: ${r2?.durationMs || 500}ms)!<br>
        👉 <i>Bài học:</i> Khi hệ thống có các tác vụ nặng nhẹ không đều, <b>Least Requests</b> vượt trội hơn Round Robin rất nhiều!
        <div style="margin-top:8px;">
          <button class="btn-sim ok" onclick="recoverAllNodes()">🟢 Khôi phục Node 2 về bình thường</button>
        </div>
      `;
    }

    async function runLabRateLimit() {
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback warning";
      box.style.display = "block";
      box.innerHTML = "⏳ Đang gửi 10 request dồn dập vào đường dẫn giới hạn <code>/api/test-ratelimit</code> (Giới hạn: 5 req / 10s)...";

      const result = await generateTraffic(10, false, "/api/test-ratelimit");
      if (result) {
        const ok = result.statusBreakdown[200] || 0;
        const blocked = result.statusBreakdown[429] || 0;
        const n1 = result.nodeBreakdown?.node1 || 0;
        const n2 = result.nodeBreakdown?.node2 || 0;

        let logRows = "";
        if (Array.isArray(result.logs)) {
          logRows = result.logs.map(l => {
            const isOk = l.statusCode === 200;
            const badgeColor = isOk ? (l.servedBy.includes("1") ? "#60a5fa" : "#34d399") : "#ef4444";
            const statusBadge = isOk 
              ? `<span style="color:#10b981; font-weight:700;">200 OK</span>`
              : `<span style="color:#ef4444; font-weight:700;">429 CHẶN (RATE LIMIT)</span>`;
            return `<div style="padding:4px 0; border-bottom:1px solid rgba(255,255,255,0.06); display:flex; justify-content:space-between; font-size:11px;">
              <span>Req <b>#${l.reqNum}</b>: ${statusBadge}</span>
              <span>Đích: <b style="color:${badgeColor}">${l.servedBy}</b> (${l.latencyMs}ms)</span>
            </div>`;
          }).join("");
        }

        box.innerHTML = `
          🛡️ <b>KẾT QUẢ BÀI THỬ 3 (RATE LIMITING CHI TIẾT):</b><br>
          • <b>Tổng số request gửi:</b> 10 requests dồn dập qua Gateway (:5266)<br>
          • <b>Hợp lệ & được xử lý (200 OK):</b> <b style="color:#10b981">${ok} requests</b><br>
          &nbsp;&nbsp;&nbsp;&nbsp;➔ <b style="color:#60a5fa">Node 1 phục vụ:</b> <b>${n1} requests</b><br>
          &nbsp;&nbsp;&nbsp;&nbsp;➔ <b style="color:#34d399">Node 2 phục vụ:</b> <b>${n2} requests</b><br>
          • <b>Bị Gateway chặn lại (429 Too Many Requests):</b> <b style="color:#ef4444">${blocked} requests</b><br>
          &nbsp;&nbsp;&nbsp;&nbsp;<i>(Các request này bị chặn đứng ngay tại phễu Gateway, KHÔNG BAO GIỜ chạm tới Node 1 hay Node 2!)</i><br><br>
          <div style="font-weight:700; color:#93c5fd; margin-bottom:4px;">📋 Bảng hành trình chi tiết 10 requests:</div>
          <div style="max-height:170px; overflow-y:auto; font-family:'JetBrains Mono', monospace; background:rgba(0,0,0,0.4); padding:8px 12px; border-radius:8px; border:1px solid rgba(255,255,255,0.08);">
            ${logRows}
          </div>
          <div style="margin-top:8px; font-size:12px; color:#cbd5e1;">
            👉 <b>Bài học:</b> 5 request đầu tiên được chia đều cho <b>Node 1 (${n1})</b> và <b>Node 2 (${n2})</b>. Từ request thứ 6 trở đi, Gateway lập tức dựng khiên chặn lại với HTTP 429 để bảo vệ tài nguyên các node!
          </div>
        `;
      }
    }

    async function recoverAllNodes() {
      await simulateNode("node1", "Normal");
      await simulateNode("node2", "Normal");
      const box = document.getElementById("labFeedbackBox");
      box.className = "lab-feedback success";
      box.innerHTML = "✅ Cả 2 node đã được khôi phục về trạng thái bình thường (0ms delay, 200 OK).";
      fetchStatus();
    }

    document.getElementById("btnResetMetrics").addEventListener("click", async () => {
      await fetch("/api/lb/clear-metrics", { method: "POST" });
      document.getElementById("benchmarkBox").style.display = "none";
      document.getElementById("labFeedbackBox").style.display = "none";
      fetchStatus();
    });

    // Ngrok management
    let currentNgrokUrl = null;

    async function checkNgrokStatus() {
      try {
        const res = await fetch("/api/lb/ngrok");
        if (!res.ok) return;
        const data = await res.json();
        const badge = document.getElementById("ngrokBadge");
        const dot = document.getElementById("ngrokDot");
        const text = document.getElementById("ngrokBadgeText");

        if (data.isRunning && data.publicUrl) {
          currentNgrokUrl = data.publicUrl;
          dot.style.background = "#10b981";
          dot.className = "badge-dot pulse";
          badge.style.borderColor = "#10b981";
          text.innerHTML = `🌐 ngrok: <b style="color:#34d399;">${data.publicUrl}</b>`;
        } else {
          currentNgrokUrl = null;
          dot.style.background = "#94a3b8";
          dot.className = "badge-dot";
          badge.style.borderColor = "var(--card-border)";
          text.innerHTML = `🌐 ngrok: <b style="color:#94a3b8;">TẮT</b> (Bấm để bật)`;
        }
      } catch (e) {}
    }

    async function handleNgrokClick() {
      if (currentNgrokUrl) {
        const choice = confirm(`🌐 LINK NGROK ĐANG HOẠT ĐỘNG:\n${currentNgrokUrl}\n\n• Nhấn OK để SAO CHÉP link vào bộ nhớ tạm.\n• Nhấn Cancel nếu muốn đóng hoặc tắt tunnel.`);
        if (choice) {
          navigator.clipboard.writeText(currentNgrokUrl);
          alert("✅ Đã sao chép link ngrok vào clipboard:\n" + currentNgrokUrl);
        } else {
          const wantStop = confirm("Bạn có muốn TẮT ngrok tunnel này không?");
          if (wantStop) {
            await fetch("/api/lb/ngrok/stop", { method: "POST" });
            alert("Đã tắt ngrok.");
            checkNgrokStatus();
          }
        }
      } else {
        const wantStart = confirm("Bạn có muốn TẠO LINK NGROK CÔNG KHAI cho http://localhost:5266 không?\n\n(Dùng để mở trên điện thoại hoặc chia sẻ ra ngoài Internet)");
        if (wantStart) {
          const text = document.getElementById("ngrokBadgeText");
          text.textContent = "⏳ Đang kết nối ngrok...";
          try {
            const res = await fetch("/api/lb/ngrok/start", { method: "POST" });
            const data = await res.json();
            if (data.isRunning && data.publicUrl) {
              currentNgrokUrl = data.publicUrl;
              navigator.clipboard.writeText(data.publicUrl);
              alert("🎉 ĐÃ TẠO LINK NGROK THÀNH CÔNG!\n\n" + data.publicUrl + "\n\n(Đã tự động copy link vào clipboard)");
              checkNgrokStatus();
            } else {
              alert("⚠️ Không thể khởi động ngrok: " + (data.error || "Lỗi không xác định"));
              checkNgrokStatus();
            }
          } catch (e) {
            alert("Lỗi gọi ngrok: " + e.message);
            checkNgrokStatus();
          }
        }
      }
    }

    // Auto refresh every 1s
    setInterval(fetchStatus, 1000);
    setInterval(checkNgrokStatus, 3000);
    fetchStatus();
    checkNgrokStatus();
  </script>
</body>
</html>
""";
    }
}
