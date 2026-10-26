using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;

namespace AegisWAF.Gateway.Sites;

internal sealed class SiteRoutingHealthCheck(SiteRegistry registry, IProxyStateLookup proxy, IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (registry.Current.UsedLegacyConfiguration) return Task.FromResult(HealthCheckResult.Healthy());

        var unavailable = new List<string>();
        var requireKnownHealthy = configuration.GetValue("Waf:Sites:RequireKnownHealthy", false);
        foreach (var site in registry.Current.Sites.Where(x => x.Enabled))
        {
            if (!proxy.TryGetCluster("site-" + site.Id, out var cluster))
            {
                unavailable.Add(site.Id);
                continue;
            }
            var available = cluster.DestinationsState.AllDestinations.Any(destination =>
                SiteProxyHealth.IsAvailable(destination.Health.Active.ToString(), destination.Health.Passive.ToString(), requireKnownHealthy));
            if (!available) unavailable.Add(site.Id);
        }

        return unavailable.Count == 0
            ? Task.FromResult(HealthCheckResult.Healthy())
            : Task.FromResult(HealthCheckResult.Unhealthy("No available upstream destination for: " + string.Join(",", unavailable)));
    }
}
