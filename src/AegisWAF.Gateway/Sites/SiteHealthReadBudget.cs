namespace AegisWAF.Gateway.Sites;

/// <summary>Bounds database reads for individual Multi-Site list and health requests.</summary>
internal static class SiteHealthReadBudget
{
    public const int DefaultMilliseconds = 2_000;

    public static CancellationTokenSource Create(CancellationToken requestAborted, int milliseconds = DefaultMilliseconds)
    {
        if (milliseconds < 1) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var budget = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        budget.CancelAfter(milliseconds);
        return budget;
    }

    public static bool IsBudgetCancellation(CancellationToken requestAborted, CancellationToken budgetToken) =>
        budgetToken.IsCancellationRequested && !requestAborted.IsCancellationRequested;
}
