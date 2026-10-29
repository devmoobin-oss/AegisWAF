using System.Threading.Channels;
using Npgsql;

namespace AegisWAF.Gateway.Sites;

/// <summary>
/// Listens for committed Site mutations and converges this Gateway instance on the
/// authoritative PostgreSQL snapshot. Notification payloads are wake-up hints only.
/// </summary>
public sealed class SiteRuntimeChangeListener(
    SiteDatabaseStore store,
    SiteRuntimeCoordinator runtime,
    IConfiguration configuration,
    SiteRuntimeListenerStatus status,
    ILogger<SiteRuntimeChangeListener> logger) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Waf:Sites:UseDatabase", true))
        {
            status.MarkDisabled();
            logger.LogInformation("Site runtime change listener disabled because database-backed Sites are disabled");
            return;
        }

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            status.MarkDisabled();
            logger.LogInformation("Site runtime change listener disabled because PostgreSQL is not configured");
            return;
        }

        var retryDelay = InitialRetryDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                status.MarkConnecting();
                await ListenUntilDisconnectedAsync(connectionString, stoppingToken);
                retryDelay = InitialRetryDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                status.MarkDisconnected(ex);
                logger.LogWarning("Site runtime change listener disconnected ({ErrorType}); retrying", ex.GetType().Name);
                try { await Task.Delay(retryDelay, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                retryDelay = TimeSpan.FromSeconds(Math.Min(MaxRetryDelay.TotalSeconds, retryDelay.TotalSeconds * 2));
            }
        }
        status.MarkStopped();
    }

    private async Task ListenUntilDisconnectedAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        using (var connectionBudget = SiteRuntimeListenerConnectionBudget.Create(ct))
        {
            try
            {
                await connection.OpenAsync(connectionBudget.Token);
                await using var listen = new NpgsqlCommand($"LISTEN {SiteRuntimeChangeNotification.Channel};", connection);
                await listen.ExecuteNonQueryAsync(connectionBudget.Token);
            }
            catch (OperationCanceledException) when (SiteRuntimeListenerConnectionBudget.IsBudgetCancellation(ct, connectionBudget.Token))
            {
                throw new TimeoutException("Site runtime listener connection setup exceeded its bounded window.");
            }
        }

        status.MarkConnected();

        var wakeups = Channel.CreateBounded<bool>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        void OnNotification(object? _, NpgsqlNotificationEventArgs args)
        {
            if (SiteRuntimeChangeNotification.TryParse(args.Payload, out SiteRuntimeChange change)) wakeups.Writer.TryWrite(true);
        }

        connection.Notification += OnNotification;
        try
        {
            await ReloadSnapshotAsync(ct); // catch up with commits that raced startup/listen
            while (!ct.IsCancellationRequested)
            {
                await connection.WaitAsync(ct);
                if (!wakeups.Reader.TryRead(out _)) continue;
                while (wakeups.Reader.TryRead(out _)) { }
                await ReloadSnapshotWithRetryAsync(ct);
            }
        }
        finally
        {
            connection.Notification -= OnNotification;
            wakeups.Writer.TryComplete();
        }
    }

    private async Task ReloadSnapshotAsync(CancellationToken ct)
    {
        using var reloadBudget = SiteRuntimeReloadBudget.Create(ct);
        try
        {
            await runtime.ActivateFromStoreAsync(store, reloadBudget.Token);
        }
        catch (OperationCanceledException) when (SiteRuntimeReloadBudget.IsBudgetCancellation(ct, reloadBudget.Token))
        {
            throw new TimeoutException("Site runtime snapshot activation exceeded its bounded reload window.");
        }

        status.MarkReloaded();
        logger.LogInformation("Site runtime snapshot activated from PostgreSQL");
    }

    private Task ReloadSnapshotWithRetryAsync(CancellationToken ct) =>
        SiteRuntimeReloadRetry.RunAsync(
            ReloadSnapshotAsync,
            (exception, willRetry) =>
            {
                status.MarkReloadFailure(exception);
                logger.LogWarning(
                    "Site runtime snapshot reload failed ({ErrorType}); {NextAction}",
                    exception.GetType().Name,
                    willRetry ? "retrying" : "waiting for the next Site change");
            },
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            ct);
}
