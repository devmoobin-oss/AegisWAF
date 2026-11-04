using AegisWAF.Gateway;

namespace AegisWAF.Desktop;

/// <summary>
/// Owns the in-process gateway. There is no child process: Kestrel runs on thread-pool threads of this app and is
/// stopped when the app exits or the settings change.
/// </summary>
public sealed class LocalGateway : IDisposable
{
    private readonly EmbeddedGateway _gateway = new();

    public bool Running => _gateway.Running;
    public string RecentOutput => _gateway.RecentLog;

    public Task StartAsync(GatewaySettings s, CancellationToken ct = default) =>
        // Task.Run keeps the gateway's continuations off the WPF dispatcher.
        Task.Run(() => _gateway.StartAsync(new EmbeddedGatewayOptions(s.ListenUrl, s.Db.ToConnectionString(), null, s.UpstreamUrl, WebAssetStore.EnsureExtracted()), ct), ct);

    public Task StopAsync() => Task.Run(() => _gateway.StopAsync());

    public void Dispose() => StopAsync().GetAwaiter().GetResult(); // on the pool, so no dispatcher deadlock
}
