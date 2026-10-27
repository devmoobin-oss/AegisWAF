using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteMutationWriteBudgetTests
{
    [Fact]
    public async Task Mutation_write_budget_cancels_within_the_configured_window()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationWriteBudget.Create(request.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(request.IsCancellationRequested);
    }

    [Fact]
    public void Request_cancellation_is_not_misclassified_as_mutation_write_timeout()
    {
        using var request = new CancellationTokenSource();
        using var budget = SiteMutationWriteBudget.Create(request.Token, 5_000);
        request.Cancel();

        Assert.False(SiteMutationWriteBudget.IsBudgetCancellation(request.Token, budget.Token));
    }

    [Fact]
    public void Site_create_update_and_delete_writes_use_the_bounded_budget()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var create = Endpoint(source, "app.MapPost(\"/api/sites\"", "app.MapPut(\"/api/sites/{id}\"");
        var update = Endpoint(source, "app.MapPut(\"/api/sites/{id}\"", "app.MapDelete(\"/api/sites/{id}\"");
        var delete = Endpoint(source, "app.MapDelete(\"/api/sites/{id}\"", "app.MapAuth()");

        foreach (var endpoint in new[] { create, update, delete })
        {
            Assert.Contains("CancellationTokenSource? writeBudget = null;", endpoint, StringComparison.Ordinal);
            Assert.Contains("writeBudget = SiteMutationWriteBudget.Create(ct);", endpoint, StringComparison.Ordinal);
            Assert.Contains("SiteMutationWriteBudget.IsBudgetCancellation(ct, writeBudget.Token)", endpoint, StringComparison.Ordinal);
            Assert.Contains("site_mutation_write_timeout", endpoint, StringComparison.Ordinal);
            Assert.Contains("writeBudget?.Dispose();", endpoint, StringComparison.Ordinal);
        }

        Assert.Contains("store.InsertAsync", create, StringComparison.Ordinal);
        Assert.Contains("store.UpdateWithConcurrencyAsync", update, StringComparison.Ordinal);
        Assert.Contains("store.DeleteWithConcurrencyAsync", delete, StringComparison.Ordinal);
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
