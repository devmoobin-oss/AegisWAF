using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace AegisWAF.Gateway;

public sealed record EmbeddedGatewayOptions(
    Uri ListenUrl,
    string ConnectionString,
    string? BootstrapAdminPassword,
    string? UpstreamUrl,
    string? ContentRoot = null,
    IReadOnlyDictionary<string, string?>? ConfigurationOverrides = null);

/// <summary>
/// Runs the gateway inside the host process (the desktop app) on background threads: no child process and no console window.
/// Startup failures (database unreachable, port in use, ...) surface as exceptions from <see cref="StartAsync"/>.
/// </summary>
public sealed class EmbeddedGateway : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<string> _log = new();
    private WebApplication? _app;

    public bool Running => _app is not null;

    /// <summary>Most recent warning/error log lines, for diagnostics shown in the UI.</summary>
    public string RecentLog { get { lock (_log) return string.Join(Environment.NewLine, _log); } }

    public async Task StartAsync(EmbeddedGatewayOptions o, CancellationToken ct = default)
    {
        lock (_gate) if (_app is not null) return;

        var overrides = new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = o.ConnectionString };
        if (!string.IsNullOrEmpty(o.BootstrapAdminPassword)) overrides["Bootstrap:AdminPassword"] = o.BootstrapAdminPassword;
        if (!string.IsNullOrWhiteSpace(o.UpstreamUrl)) overrides["ReverseProxy:Clusters:appcluster:Destinations:app1:Address"] = o.UpstreamUrl;
        if (o.ConfigurationOverrides is not null)
            foreach (var pair in o.ConfigurationOverrides) overrides[pair.Key] = pair.Value;

        var builder = GatewayApp.CreateBuilder([], contentRoot: o.ContentRoot ?? AppContext.BaseDirectory, overrides);
        builder.WebHost.UseUrls(o.ListenUrl.GetLeftPart(UriPartial.Authority));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new BufferLoggerProvider(_log));
        GatewayApp.AddServices(builder);
        var app = builder.Build();
        try
        {
            await Bootstrap.RunAsync(app).ConfigureAwait(false);
            GatewayApp.MapPipeline(app);
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        lock (_gate) _app = app;
    }

    public async Task StopAsync()
    {
        WebApplication? app;
        lock (_gate) { app = _app; _app = null; }
        if (app is null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await app.StopAsync(cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* forced */ }
        await app.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private sealed class BufferLoggerProvider(Queue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new BufferLogger(sink, categoryName);
        public void Dispose() { }
    }

    private sealed class BufferLogger(Queue<string> sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt)
        {
            if (!IsEnabled(level)) return;
            var text = $"{level}: {category}: {fmt(state, ex)}" + (ex is null ? "" : " — " + ex.GetType().Name + ": " + ex.Message);
            lock (sink) { sink.Enqueue(text.Length > 400 ? text[..400] : text); while (sink.Count > 20) sink.Dequeue(); }
        }
    }
}
