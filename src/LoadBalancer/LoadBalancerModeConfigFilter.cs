using Yarp.ReverseProxy.Configuration;

namespace LoadBalancer;

/// <summary>
/// Ap dung thuat toan Load Balancing va che do Session Affinity dong luc runtime.
/// Khi LbState thay doi, SignalConfigurationProvider kich hoat reload de YARP cap nhat tuc thi.
/// </summary>
public sealed class LoadBalancerModeConfigFilter(LbState state) : IProxyConfigFilter
{
    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        // Thuat toan duoc chon (RoundRobin, LeastRequests, Random, PowerOfTwoChoices)
        cluster = cluster with { LoadBalancingPolicy = state.Algorithm };

        if (string.Equals(state.Mode, "Affinity", StringComparison.OrdinalIgnoreCase))
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
