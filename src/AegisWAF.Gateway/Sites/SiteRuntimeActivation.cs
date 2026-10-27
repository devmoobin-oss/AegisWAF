using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Gateway.Sites;

/// <summary>
/// Configuration provider owned by the Site runtime. YARP continues to consume the
/// normal ReverseProxy configuration tree, while this provider supplies the current
/// database-backed Site routes and emits a reload token after a successful mutation.
/// </summary>
public sealed class SiteRuntimeConfigurationProvider : ConfigurationProvider
{
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string?> _current;

    public SiteRuntimeConfigurationProvider(IReadOnlyDictionary<string, string?> initialValues)
    {
        _current = Snapshot(initialValues);
    }

    public IReadOnlyDictionary<string, string?> Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Replace(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_gate)
        {
            _current = Snapshot(values);
            Data = new Dictionary<string, string?>(_current, StringComparer.OrdinalIgnoreCase);
            OnReload();
        }
    }

    public override void Load()
    {
        Data = new Dictionary<string, string?>(_current, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string?> Snapshot(IReadOnlyDictionary<string, string?> values) =>
        new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase));
}

public sealed class SiteRuntimeConfigurationSource(SiteRuntimeConfigurationProvider provider) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
}

/// <summary>Publishes a validated Site snapshot to both the request matcher and YARP.</summary>
public sealed class SiteRuntimeCoordinator(
    SiteRegistry registry,
    SiteRuntimeConfigurationProvider provider,
    IConfiguration configuration)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Activate(SiteConfigurationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _gate.Wait();
        try { ActivateCore(snapshot); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Loads the authoritative database snapshot while holding the same gate used
    /// for activation. This prevents a slower stale load from replacing a newer
    /// snapshot after a concurrent Site mutation.
    /// </summary>
    public Task ActivateFromStoreAsync(SiteDatabaseStore store, CancellationToken ct) =>
        ActivateFromSnapshotLoaderAsync(store.ListAsync, ct);

    internal async Task ActivateFromSnapshotLoaderAsync(
        Func<CancellationToken, Task<IReadOnlyList<GatewaySite>>> loader,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loader);
        await _gate.WaitAsync(ct);
        try
        {
            var sites = await loader(ct);
            ActivateCore(new SiteConfigurationSnapshot(sites, false));
        }
        finally { _gate.Release(); }
    }

    private void ActivateCore(SiteConfigurationSnapshot snapshot)
    {
        var errors = SiteConfigurationValidator.ValidateSnapshot(snapshot, configuration);
        if (errors.Count > 0)
            throw new SiteRuntimeActivationException(string.Join("; ", errors));

        var routeValues = SiteProxyConfiguration.Build(snapshot, configuration);
        provider.Replace(routeValues);
        registry.Replace(snapshot);
    }
}

public sealed class SiteRuntimeActivationException(string message) : InvalidOperationException(message);
