using System.Net;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SiteRuntimeDistributedTests(GatewayFixture fx)
{
    [Fact]
    public async Task Peer_gateway_activates_a_committed_site_without_restart()
    {
        using var peer = fx.CreatePeer();
        var admin = await fx.LoginAdmin();
        var id = "peer-site-" + Guid.NewGuid().ToString("N")[..6];
        var payload = new
        {
            id,
            hostnames = new[] { id + ".example.test" },
            pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/",
            wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" },
            allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" },
            enabled = true
        };

        try
        {
            var created = await admin.Send(HttpMethod.Post, "/api/sites", payload);
            created.EnsureSuccessStatusCode();

            var peerRegistry = peer.Services.GetRequiredService<SiteRegistry>();
            await WaitUntilAsync(() => peerRegistry.Current.Sites.Any(site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
            Assert.Contains(peerRegistry.Current.Sites, site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            var deleted = await admin.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: "\"1\"");
            Assert.Contains(deleted.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        }
    }

    [Fact]
    public async Task Peer_keeps_last_known_good_route_when_a_persisted_snapshot_is_invalid_then_recovers()
    {
        using var peer = fx.CreatePeer();
        var admin = await fx.LoginAdmin();
        var id = "peer-invalid-" + Guid.NewGuid().ToString("N")[..6];
        var payload = new
        {
            id,
            hostnames = new[] { id + ".example.test" },
            pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/",
            wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" },
            allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" },
            enabled = true
        };

        try
        {
            var created = await admin.Send(HttpMethod.Post, "/api/sites", payload);
            created.EnsureSuccessStatusCode();

            var peerRegistry = peer.Services.GetRequiredService<SiteRegistry>();
            var listenerStatus = peer.Services.GetRequiredService<SiteRuntimeListenerStatus>();
            await WaitUntilAsync(() => peerRegistry.Current.Sites.Any(site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
            var knownGood = peerRegistry.Current.Sites.Single(site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

            await MutatePersistedUpstreamAndNotifyAsync(fx.ConnectionString, id, "not-an-http-url");
            await WaitUntilAsync(() => listenerStatus.Current.LastErrorType == nameof(InvalidDataException));

            var retained = peerRegistry.Current.Sites.Single(site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(knownGood.Upstreams.Single().Address, retained.Upstreams.Single().Address);

            await MutatePersistedUpstreamAndNotifyAsync(fx.ConnectionString, id, "https://origin.example.test/");
            var previousReload = listenerStatus.Current.LastReloadAt;
            await WaitUntilAsync(() => listenerStatus.Current.LastReloadAt is not null &&
                                       (previousReload is null || listenerStatus.Current.LastReloadAt > previousReload));

            var recovered = peerRegistry.Current.Sites.Single(site => site.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new Uri("https://origin.example.test/"), recovered.Upstreams.Single().Address);
        }
        finally
        {
            var deleted = await admin.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: "\"1\"");
            Assert.Contains(deleted.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Conflict });
        }
    }

    private static async Task MutatePersistedUpstreamAndNotifyAsync(string connectionString, string siteId, string address)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await using (var update = new NpgsqlCommand("UPDATE waf_sites SET upstream_addresses=@address WHERE site_id=@site_id", db))
        {
            update.Parameters.AddWithValue("address", new[] { address });
            update.Parameters.AddWithValue("site_id", siteId);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        await using var notify = new NpgsqlCommand("SELECT pg_notify(@channel, @payload)", db);
        notify.Parameters.AddWithValue("channel", SiteRuntimeChangeNotification.Channel);
        notify.Parameters.AddWithValue("payload", SiteRuntimeChangeNotification.CreatePayload("site.update", siteId));
        await notify.ExecuteNonQueryAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(50, timeout.Token);
        }
    }
}
