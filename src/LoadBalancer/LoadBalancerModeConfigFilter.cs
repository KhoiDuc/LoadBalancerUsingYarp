using Yarp.ReverseProxy.Configuration;

namespace LoadBalancer;

/// <summary>
/// Chon 1 trong 2 luong bang setting "LoadBalancer:Mode" luc runtime:
///   - "Stateless" (mac dinh): RoundRobin, KHONG affinity.
///   - "Affinity": RoundRobin + sticky cookie -> moi user "dinh" 1 node.
/// Doi gia tri trong appsettings.json se HOT-RELOAD (khong can restart).
/// </summary>
public sealed class LoadBalancerModeConfigFilter(IConfiguration config) : IProxyConfigFilter
{
    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        var mode = config["LoadBalancer:Mode"] ?? "Stateless";

        cluster = cluster with { LoadBalancingPolicy = "RoundRobin" };

        if (string.Equals(mode, "Affinity", StringComparison.OrdinalIgnoreCase))
        {
            cluster = cluster with
            {
                SessionAffinity = new SessionAffinityConfig
                {
                    Enabled = true,
                    Policy = "Cookie",
                    FailurePolicy = "Redistribute",
                    AffinityKeyName = "Yarp.Affinity",
                    Cookie = new SessionAffinityCookieConfig
                    {
                        HttpOnly = true,
                        // Demo chay http -> SameAsRequest. Production nen dung Always.
                        SecurePolicy = CookieSecurePolicy.SameAsRequest
                    }
                }
            };
        }
        else
        {
            cluster = cluster with { SessionAffinity = null };
        }

        return ValueTask.FromResult(cluster);
    }

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        => ValueTask.FromResult(route);
}
