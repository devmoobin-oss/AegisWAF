using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteHealthWriteBudgetTests
{
    [Fact]
    public async Task Health_write_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthWriteBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_health_write_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteHealthWriteBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteHealthWriteBudget.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Manual_health_and_acknowledgement_writes_use_the_bounded_budget()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var manual = Endpoint(source, "app.MapPost(\"/api/sites/{id}/health\"", "app.MapGet(\"/api/sites/{id}/health/history\"");
        var acknowledgement = Endpoint(source, "app.MapPost(\"/api/sites/{id}/health/alerts/acknowledge\"", "app.MapGet(\"/api/sites/health/summary\"");

        Assert.Contains("using var writeBudget = SiteHealthWriteBudget.Create(ct);", manual, StringComparison.Ordinal);
        Assert.Contains("var writeCt = writeBudget.Token;", manual, StringComparison.Ordinal);
        Assert.Contains("RecordHealthHistoryAsync", manual, StringComparison.Ordinal);
        Assert.Contains("SiteHealthWriteBudget.IsBudgetCancellation(ct, writeCt)", manual, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource? writeBudget = null;", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("writeBudget = SiteHealthWriteBudget.Create(ct);", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("AcknowledgeHealthAlertAsync", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("SiteHealthWriteBudget.IsBudgetCancellation(ct, writeBudget.Token)", acknowledgement, StringComparison.Ordinal);
        Assert.Contains("site_health_write_timeout", acknowledgement, StringComparison.Ordinal);
    }

    private static string Endpoint(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate endpoint {startMarker}.");
        return source[start..end];
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
