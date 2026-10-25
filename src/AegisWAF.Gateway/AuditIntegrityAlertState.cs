namespace AegisWAF.Gateway;

/// <summary>Deduplicates recent audit-chain integrity alerts without allowing the state to grow without bound.</summary>
public sealed class AuditIntegrityAlertState
{
    private const int MaxRememberedIds = 1024;
    private readonly object _gate = new();
    private readonly HashSet<long> _claimed = [];
    private readonly Queue<long> _order = [];

    public bool TryClaim(long invalidId)
    {
        lock (_gate)
        {
            if (!_claimed.Add(invalidId)) return false;
            _order.Enqueue(invalidId);
            while (_order.Count > MaxRememberedIds)
                _claimed.Remove(_order.Dequeue());
            return true;
        }
    }

    public void Release(long invalidId)
    {
        lock (_gate)
        {
            if (!_claimed.Remove(invalidId)) return;
            if (_order.Count > 0 && _order.Peek() == invalidId)
            {
                _order.Dequeue();
                return;
            }

            var retained = _order.Where(id => id != invalidId).ToArray();
            _order.Clear();
            foreach (var id in retained) _order.Enqueue(id);
        }
    }
}
