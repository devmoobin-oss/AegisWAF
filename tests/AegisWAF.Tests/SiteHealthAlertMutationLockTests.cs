using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthAlertMutationLockTests
{
    [Fact]
    public void Health_alert_acknowledgement_uses_the_bounded_site_mutation_lock()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var start = gateway.IndexOf("/api/sites/{id}/health/alerts/acknowledge", StringComparison.Ordinal);
        var end = gateway.IndexOf("}).Require(Roles.Operator);", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the health alert acknowledgement endpoint.");
        var endpoint = gateway[start..end];

        Assert.Contains("AcquireSiteMutationLockAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteMutationLockTimeoutException", endpoint, StringComparison.Ordinal);
        Assert.Contains("SiteMutationBusy(ctx)", endpoint, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync", endpoint, StringComparison.Ordinal);
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
