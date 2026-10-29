using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeBatchConcurrencyContractTests
{
    [Fact]
    public void Site_health_probe_declares_a_global_batch_concurrency_gate()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteHealthProbe.cs"), Encoding.UTF8);

        Assert.Contains("MaxConcurrentBatches", source, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(ct)", source, StringComparison.Ordinal);
        Assert.Contains("_batchConcurrency.Release()", source, StringComparison.Ordinal);
        Assert.Contains("HealthProbeMaxConcurrentBatches", source, StringComparison.Ordinal);
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
