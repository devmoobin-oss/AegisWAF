using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthReadBudgetTests
{
    [Fact]
    public async Task Health_read_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthReadBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_health_read_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthReadBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteHealthReadBudget.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Health_history_trend_and_acknowledgement_reads_use_the_bounded_budget()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        Assert.Equal(3, Count(gateway, "SiteHealthReadBudget.Create"));
        Assert.True(Count(gateway, "site_health_read_timeout") >= 3);
        Assert.Contains("readBudget.Token", gateway, StringComparison.Ordinal);
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
