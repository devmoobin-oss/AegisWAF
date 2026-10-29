using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeChangeListenerBatchingContractTests
{
    [Fact]
    public void Listener_coalesces_pending_notifications_before_one_authoritative_reload()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeChangeListener.cs"),
            Encoding.UTF8);

        var firstRead = source.IndexOf("if (!wakeups.Reader.TryRead(out _)) continue;", StringComparison.Ordinal);
        Assert.True(firstRead >= 0, "The listener should ignore a wake-up without a queued parsed notification.");

        if (firstRead < 0) return;

        var drain = source.IndexOf("while (wakeups.Reader.TryRead(out _))", firstRead, StringComparison.Ordinal);
        var reload = source.IndexOf("await ReloadSnapshotWithRetryAsync(ct);", drain, StringComparison.Ordinal);
        Assert.True(drain > firstRead, "The listener should drain the rest of the current notification burst.");
        Assert.True(reload > drain, "The listener should reload once after draining the burst.");
        Assert.DoesNotContain("while (wakeups.Reader.TryRead(out _))\n                {\n                    await ReloadSnapshotWithRetryAsync(ct);\n                }", source, StringComparison.Ordinal);
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
