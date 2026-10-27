namespace AegisWAF.Gateway.Sites;

internal static class SiteMutationLockPolicy
{
    public const int DefaultWaitMilliseconds = 5_000;
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    public static CancellationTokenSource CreateBudget(CancellationToken requestAborted, int milliseconds = DefaultWaitMilliseconds)
    {
        if (milliseconds < 1) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        var budget = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        budget.CancelAfter(milliseconds);
        return budget;
    }

    public static bool IsBudgetCancellation(CancellationToken requestAborted, CancellationToken budgetToken) =>
        budgetToken.IsCancellationRequested && !requestAborted.IsCancellationRequested;
}

public sealed class SiteMutationLockTimeoutException : TimeoutException
{
    public SiteMutationLockTimeoutException()
        : base("The Site mutation lock could not be acquired within the configured wait budget.")
    {
    }
}
