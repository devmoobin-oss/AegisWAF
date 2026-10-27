using System.Text;
using AegisWAF.Gateway.Sites;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeReloadBudgetTests
{
    [Fact]
    public async Task Runtime_reload_budget_cancels_within_the_configured_window()
    {
        using var listener = new CancellationTokenSource();
        using var budget = SiteRuntimeReloadBudget.Create(listener.Token, 20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, budget.Token));
        Assert.True(budget.Token.IsCancellationRequested);
        Assert.False(listener.IsCancellationRequested);
    }

    [Fact]
    public void Listener_shutdown_is_not_misclassified_as_reload_timeout()
    {
        using var listener = new CancellationTokenSource();
        using var budget = SiteRuntimeReloadBudget.Create(listener.Token, 5_000);
        listener.Cancel();

        Assert.False(SiteRuntimeReloadBudget.IsBudgetCancellation(listener.Token, budget.Token));
    }

    [Fact]
    public void Background_reload_uses_a_bounded_budget_and_translates_budget_expiry_to_retryable_timeout()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeChangeListener.cs"), Encoding.UTF8);

        Assert.Contains("SiteRuntimeReloadBudget.Create(ct)", source, StringComparison.Ordinal);
        Assert.Contains("runtime.ActivateFromStoreAsync(store, reloadBudget.Token)", source, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeReloadBudget.IsBudgetCancellation(ct, reloadBudget.Token)", source, StringComparison.Ordinal);
        Assert.Contains("throw new TimeoutException", source, StringComparison.Ordinal);
        Assert.Contains("exception is NpgsqlException or TimeoutException", File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeReloadRetry.cs"), Encoding.UTF8), StringComparison.Ordinal);
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
