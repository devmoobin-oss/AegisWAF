using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeListenerConnectionBudgetTests
{
    [Fact]
    public async Task Listener_connection_budget_cancels_within_the_configured_window()
    {
        using var stopping = new CancellationTokenSource();
        using var budget = SiteRuntimeListenerConnectionBudget.Create(stopping.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(stopping.IsCancellationRequested);
    }

    [Fact]
    public void Listener_shutdown_is_not_misclassified_as_connection_timeout()
    {
        using var stopping = new CancellationTokenSource();
        using var budget = SiteRuntimeListenerConnectionBudget.Create(stopping.Token, 5_000);
        stopping.Cancel();

        Assert.False(SiteRuntimeListenerConnectionBudget.IsBudgetCancellation(stopping.Token, budget.Token));
    }

    [Fact]
    public void Listener_reports_connected_only_after_listen_subscription_and_bounds_both_operations()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeChangeListener.cs"), Encoding.UTF8);
        var budgetIndex = source.IndexOf("SiteRuntimeListenerConnectionBudget.Create(ct)", StringComparison.Ordinal);
        var openIndex = source.IndexOf("connection.OpenAsync(connectionBudget.Token)", StringComparison.Ordinal);
        var listenIndex = source.IndexOf("listen.ExecuteNonQueryAsync(connectionBudget.Token)", StringComparison.Ordinal);
        var connectedIndex = source.IndexOf("status.MarkConnected();", StringComparison.Ordinal);

        Assert.True(budgetIndex >= 0);
        Assert.True(openIndex > budgetIndex);
        Assert.True(listenIndex > openIndex);
        Assert.True(connectedIndex > listenIndex);
        Assert.Contains("SiteRuntimeListenerConnectionBudget.IsBudgetCancellation(ct, connectionBudget.Token)", source, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", source, StringComparison.Ordinal);
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
