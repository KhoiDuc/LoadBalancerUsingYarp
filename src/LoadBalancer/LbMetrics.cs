using System.Collections.Concurrent;

namespace LoadBalancer;

public sealed class RequestLogItem
{
    public long Id { get; init; }
    public string Timestamp { get; init; } = "";
    public string Method { get; init; } = "";
    public string Path { get; init; } = "";
    public string Node { get; init; } = "gateway";
    public int StatusCode { get; init; }
    public long LatencyMs { get; init; }
}

public sealed class NodeMetrics
{
    public string NodeId { get; }
    public long TotalRequests => _totalRequests;
    public long SuccessRequests => _successRequests;
    public long ErrorRequests => _errorRequests;
    public double AvgLatencyMs => _totalRequests > 0 ? Math.Round((double)_totalLatencyMs / _totalRequests, 1) : 0;
    public long LastLatencyMs => _lastLatencyMs;
    public string? LastServedAt => _lastServedAt;

    private long _totalRequests;
    private long _successRequests;
    private long _errorRequests;
    private long _totalLatencyMs;
    private long _lastLatencyMs;
    private string? _lastServedAt;

    public NodeMetrics(string nodeId)
    {
        NodeId = nodeId;
    }

    public void Record(int statusCode, long latencyMs)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Add(ref _totalLatencyMs, latencyMs);
        Interlocked.Exchange(ref _lastLatencyMs, latencyMs);
        Interlocked.Exchange(ref _lastServedAt, DateTime.Now.ToString("HH:mm:ss"));

        if (statusCode >= 200 && statusCode < 400)
        {
            Interlocked.Increment(ref _successRequests);
        }
        else
        {
            Interlocked.Increment(ref _errorRequests);
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _successRequests, 0);
        Interlocked.Exchange(ref _errorRequests, 0);
        Interlocked.Exchange(ref _totalLatencyMs, 0);
        Interlocked.Exchange(ref _lastLatencyMs, 0);
        Interlocked.Exchange(ref _lastServedAt, null);
    }
}

public sealed class LbMetrics
{
    private long _globalRequestId;
    private long _totalGatewayRequests;
    private long _totalRateLimited;
    private readonly ConcurrentDictionary<string, NodeMetrics> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RequestLogItem> _recentLogs = new();
    private const int MaxLogItems = 50;

    public LbMetrics()
    {
        _nodes["node1"] = new NodeMetrics("node1");
        _nodes["node2"] = new NodeMetrics("node2");
    }

    public void RecordRequest(string? destinationId, string method, string path, int statusCode, long latencyMs)
    {
        long reqId = Interlocked.Increment(ref _globalRequestId);
        Interlocked.Increment(ref _totalGatewayRequests);

        if (statusCode == StatusCodes.Status429TooManyRequests)
        {
            Interlocked.Increment(ref _totalRateLimited);
        }

        string node = string.IsNullOrWhiteSpace(destinationId) ? "gateway" : destinationId;
        if (!string.IsNullOrWhiteSpace(destinationId))
        {
            var nodeMetrics = _nodes.GetOrAdd(destinationId, id => new NodeMetrics(id));
            nodeMetrics.Record(statusCode, latencyMs);
        }

        var item = new RequestLogItem
        {
            Id = reqId,
            Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
            Method = method,
            Path = path,
            Node = node,
            StatusCode = statusCode,
            LatencyMs = latencyMs
        };

        _recentLogs.Enqueue(item);
        while (_recentLogs.Count > MaxLogItems && _recentLogs.TryDequeue(out _)) { }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _globalRequestId, 0);
        Interlocked.Exchange(ref _totalGatewayRequests, 0);
        Interlocked.Exchange(ref _totalRateLimited, 0);
        foreach (var node in _nodes.Values)
        {
            node.Reset();
        }
        while (_recentLogs.TryDequeue(out _)) { }
    }

    public object GetSnapshot()
    {
        return new
        {
            totalGatewayRequests = _totalGatewayRequests,
            totalRateLimited = _totalRateLimited,
            nodes = _nodes.Values.Select(n => new
            {
                nodeId = n.NodeId,
                totalRequests = n.TotalRequests,
                successRequests = n.SuccessRequests,
                errorRequests = n.ErrorRequests,
                avgLatencyMs = n.AvgLatencyMs,
                lastLatencyMs = n.LastLatencyMs,
                lastServedAt = n.LastServedAt
            }),
            recentLogs = _recentLogs.Reverse().Take(30)
        };
    }
}
