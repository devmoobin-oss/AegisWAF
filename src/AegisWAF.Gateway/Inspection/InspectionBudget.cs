using System.Diagnostics;

namespace AegisWAF.Gateway;

internal sealed class InspectionBudget
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly int _timeoutMilliseconds;

    public InspectionBudget(int timeoutMilliseconds) => _timeoutMilliseconds = Math.Max(0, timeoutMilliseconds);

    public bool Expired => HasExpired(_clock.ElapsedMilliseconds, _timeoutMilliseconds);

    public static bool HasExpired(long elapsedMilliseconds, int timeoutMilliseconds) =>
        elapsedMilliseconds >= Math.Max(0, timeoutMilliseconds);
}
