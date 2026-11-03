using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteMutationLockBudgetTests
{
    [Fact]
    public async Task Mutation_lock_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationLockPolicy.CreateBudget(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_lock_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationLockPolicy.CreateBudget(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteMutationLockPolicy.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Site_mutations_use_non_blocking_lock_polling_and_return_busy_problem_details()
    {
        var root = FindRepoRoot();
        var store = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        Assert.Contains("pg_try_advisory_xact_lock", store, StringComparison.Ordinal);
        Assert.DoesNotContain("pg_try_advisory_lock(", store, StringComparison.Ordinal);
        Assert.Contains("SiteMutationLockTimeoutException", store, StringComparison.Ordinal);
        Assert.Equal(4, Count(gateway, "SiteMutationLockTimeoutException"));
        Assert.Contains("site_mutation_busy", gateway, StringComparison.Ordinal);
    }

    private static int Count(string value, string token) => value.Split(token, StringSplitOptions.None).Length - 1;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
