using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Hosting;

namespace AegisWAF.Gateway;

/// <summary>
/// Resolves and evaluates every configured Production upstream before the host begins accepting traffic.
/// Runtime connection checks remain mandatory because DNS can change after startup.
/// </summary>
internal sealed class ProductionEgressStartupValidator(
    SiteConfigurationSnapshot snapshot,
    UpstreamEgressPolicy policy) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var site in snapshot.Sites.Where(x => x.Enabled))
        {
            foreach (var upstream in site.Upstreams)
            {
                var decision = await policy.CheckAsync(upstream.Address, site.Egress, cancellationToken).ConfigureAwait(false);
                if (!decision.Allowed)
                    throw new InvalidOperationException($"Production egress preflight rejected Site {site.Id} upstream {upstream.Id}: {decision.ReasonCode}");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
