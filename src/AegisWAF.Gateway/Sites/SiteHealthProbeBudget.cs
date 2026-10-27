namespace AegisWAF.Gateway.Sites;

/// <summary>Bounds the aggregate duration of an on-demand Multi-Site health probe batch.</summary>
internal static class SiteHealthProbeBudget
{
    public const int DefaultMilliseconds = 10_000;

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
