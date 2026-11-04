namespace AegisWAF.Gateway.Sites;

/// <summary>Bounds PostgreSQL writes performed by interactive Multi-Site health actions.</summary>
internal static class SiteHealthWriteBudget
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
