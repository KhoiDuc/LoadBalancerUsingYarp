using Yarp.ReverseProxy.Health;
using Yarp.ReverseProxy.Model;

namespace LoadBalancer;

/// <summary>
/// Chinh sach Passive Health Check thong minh:
/// Khi mot node tra ve HTTP 5xx (vi du 503 Service Unavailable),
/// danh dau ngay lap tuc node do la Unhealthy de YARP loai bo va chuyen traffic sang node con lai.
/// </summary>
public sealed class FailOn5xxPassiveHealthPolicy(IDestinationHealthUpdater updater) : IPassiveHealthCheckPolicy
{
    public string Name => "FailOn5xx";

    public void RequestProxied(HttpContext context, ClusterState cluster, DestinationState destination)
    {
        // Neu request tra ve loi 5xx (500, 502, 503, 504...)
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var reactivationPeriod = cluster.Model.Config.HealthCheck?.Passive?.ReactivationPeriod ?? TimeSpan.FromSeconds(15);
            updater.SetPassive(cluster, destination, DestinationHealth.Unhealthy, reactivationPeriod);

            var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Failover");
            logger?.LogWarning(">> [FAILOVER] Node {Node} tra ve HTTP {Code} -> Danh dau Unhealthy ngay lap tuc! Loai khoi cluster de bao ve nguoi dung.",
                destination.DestinationId, context.Response.StatusCode);
        }
    }
}
