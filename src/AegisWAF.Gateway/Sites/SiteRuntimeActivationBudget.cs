namespace AegisWAF.Gateway.Sites;

/// <summary>Bounds interactive runtime route activation after a Site mutation is committed.</summary>
internal static class SiteRuntimeActivationBudget
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
