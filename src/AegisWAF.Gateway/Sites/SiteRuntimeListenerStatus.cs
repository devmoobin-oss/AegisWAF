namespace AegisWAF.Gateway.Sites;

/// <summary>Redacted, bounded runtime state for the PostgreSQL Site-change listener.</summary>
public sealed record SiteRuntimeListenerStatusSnapshot(
    string State,
    bool IsConnected,
    int ConnectionAttempts,
    int ConsecutiveFailures,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastReloadAt,
    string? LastErrorType,
    bool HasReloadFailure);

/// <summary>
/// Keeps operator-facing listener state separate from request routing state. Only exception
/// type names are retained; connection strings and database error text never enter the snapshot.
/// </summary>
public sealed class SiteRuntimeListenerStatus
{
    private readonly object _gate = new();
    private string _state = "starting";
    private bool _isConnected;
    private int _connectionAttempts;
    private int _consecutiveFailures;
    private DateTimeOffset? _lastConnectedAt;
    private DateTimeOffset? _lastDisconnectedAt;
    private DateTimeOffset? _lastReloadAt;
    private string? _lastErrorType;
    private bool _hasReloadFailure;

    public SiteRuntimeListenerStatusSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return new SiteRuntimeListenerStatusSnapshot(
                    _state,
                    _isConnected,
                    _connectionAttempts,
                    _consecutiveFailures,
                    _lastConnectedAt,
                    _lastDisconnectedAt,
                    _lastReloadAt,
                    _lastErrorType,
                    _hasReloadFailure);
            }
        }
    }

    public void MarkConnecting()
    {
        lock (_gate)
        {
            _state = "connecting";
            _isConnected = false;
            _connectionAttempts++;
        }
    }

    public void MarkConnected()
    {
        lock (_gate)
        {
            _state = "connected";
            _isConnected = true;
            _consecutiveFailures = 0;
            _lastConnectedAt = DateTimeOffset.UtcNow;
        }
    }

    public void MarkDisconnected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            _state = "reconnecting";
            _isConnected = false;
            _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 1_000_000);
            _lastDisconnectedAt = DateTimeOffset.UtcNow;
            _lastErrorType = BoundedTypeName(exception);
        }
    }

    public void MarkReloadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            _state = _isConnected ? "connected" : "reconnecting";
            _lastErrorType = BoundedTypeName(exception);
            _hasReloadFailure = true;
        }
    }

    public void MarkReloaded()
    {
        lock (_gate)
        {
            _lastReloadAt = DateTimeOffset.UtcNow;
            _hasReloadFailure = false;
        }
    }

    public void MarkDisabled()
    {
        lock (_gate)
        {
            _state = "disabled";
            _isConnected = false;
            _lastErrorType = null;
            _hasReloadFailure = false;
        }
    }

    public void MarkStopped()
    {
        lock (_gate)
        {
            _state = "stopped";
            _isConnected = false;
        }
    }

    private static string BoundedTypeName(Exception exception)
    {
        var typeName = exception.GetType().Name;
        return typeName.Length <= 96 ? typeName : typeName[..96];
    }
}
