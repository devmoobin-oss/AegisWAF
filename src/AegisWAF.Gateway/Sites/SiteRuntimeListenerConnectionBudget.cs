namespace AegisWAF.Gateway.Sites;

/// <summary>Bounds listener connection and LISTEN subscription setup.</summary>
internal static class SiteRuntimeListenerConnectionBudget
{
    public const int DefaultMilliseconds = 5_000;

    public static CancellationTokenSource Create(CancellationToken listenerStopping, int milliseconds = DefaultMilliseconds)
    {
        if (milliseconds < 1) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var budget = CancellationTokenSource.CreateLinkedTokenSource(listenerStopping);
        budget.CancelAfter(milliseconds);
        return budget;
    }

    public static bool IsBudgetCancellation(CancellationToken listenerStopping, CancellationToken budgetToken) =>
        budgetToken.IsCancellationRequested && !listenerStopping.IsCancellationRequested;
}
