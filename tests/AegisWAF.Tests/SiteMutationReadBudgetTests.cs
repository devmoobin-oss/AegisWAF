using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteMutationReadBudgetTests
{
    [Fact]
    public async Task Mutation_read_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationReadBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_mutation_read_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationReadBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteMutationReadBudget.IsBudgetCancellation(request.Token, budget.Token));
    }
}
