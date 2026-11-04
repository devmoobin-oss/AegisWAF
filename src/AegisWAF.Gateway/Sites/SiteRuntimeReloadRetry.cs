using System.Net.Sockets;
using Npgsql;

namespace AegisWAF.Gateway.Sites;

/// <summary>
/// Retries only failures that can reasonably recover without a new Site mutation.
/// Invalid persisted configuration is deliberately left for the next notification.
/// </summary>
internal static class SiteRuntimeReloadRetry
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    public static async Task RunAsync(
        Func<CancellationToken, Task> operation,
        Action<Exception, bool> onFailure,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onFailure);
        ArgumentNullException.ThrowIfNull(delay);

        var retryDelay = InitialDelay;
        while (true)
        {
            try
            {
                await operation(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var willRetry = IsTransient(exception);
                onFailure(exception, willRetry);
                if (!willRetry) return;

                await delay(retryDelay, ct);
                retryDelay = NextDelay(retryDelay);
            }
        }
    }

    public static bool IsTransient(Exception exception) =>
        exception is not SiteRuntimeActivationException &&
        exception is NpgsqlException or TimeoutException or IOException or SocketException or InvalidOperationException;

    public static TimeSpan NextDelay(TimeSpan current) =>
        TimeSpan.FromSeconds(Math.Min(MaximumDelay.TotalSeconds, Math.Max(InitialDelay.TotalSeconds, current.TotalSeconds * 2)));
}
