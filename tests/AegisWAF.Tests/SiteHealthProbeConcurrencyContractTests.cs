using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthProbeConcurrencyContractTests
{
    [Fact]
    public void Health_probe_uses_bounded_parallelism_for_multiple_upstreams()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "Sites", "SiteHealthProbe.cs"), Encoding.UTF8);

        Assert.Contains("MaxConcurrentUpstreams", source, StringComparison.Ordinal);
        Assert.Contains("new SemaphoreSlim(MaxConcurrentUpstreams)", source, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll", source, StringComparison.Ordinal);
        Assert.Contains("await concurrency.WaitAsync(ct)", source, StringComparison.Ordinal);
        Assert.Contains("concurrency.Release()", source, StringComparison.Ordinal);
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
