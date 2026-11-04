using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeBudgetTests
{
    [Fact]
    public async Task Aggregate_probe_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthProbeBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_probe_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthProbeBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteHealthProbeBudget.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Manual_health_endpoint_uses_an_aggregate_probe_budget_and_safe_timeout_code()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = source.IndexOf("app.MapPost(\"/api/sites/{id}/health\"", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapGet(\"/api/sites/{id}/health/history\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate manual health endpoint.");
        var endpoint = source[start..end];

        Assert.Contains("using var probeBudget = SiteHealthProbeBudget.Create(ct);", endpoint, StringComparison.Ordinal);
        Assert.Contains("probe.ProbeAsync(site, probeBudget.Token)", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteHealthProbeBudget.IsBudgetCancellation(ct, probeBudget.Token)", endpoint, StringComparison.Ordinal);
        Assert.Contains("site_health_probe_timeout", source, StringComparison.Ordinal);
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
