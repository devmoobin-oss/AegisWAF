using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthSummaryBudgetTests
{
    [Fact]
    public async Task Summary_budget_cancels_its_database_token_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthSummaryBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_a_summary_budget_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthSummaryBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteHealthSummaryBudget.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Health_summary_endpoint_uses_the_budget_token_and_exposes_a_timeout_code()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapGet(\"/api/sites/health/summary\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapPost(\"/api/sites\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the Site health summary endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("SiteHealthSummaryBudget.Create(ct, summaryQueryBudgetMs)", endpoint, StringComparison.Ordinal);
        Assert.Contains("summaryBudget.Token", endpoint, StringComparison.Ordinal);
        Assert.Contains("site_health_summary_timeout", endpoint, StringComparison.Ordinal);
        Assert.Contains("IsBudgetCancellation(ct, summaryCt)", endpoint, StringComparison.Ordinal);
    }

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
