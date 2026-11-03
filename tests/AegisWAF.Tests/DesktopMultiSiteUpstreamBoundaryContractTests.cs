using System.Text;

namespace AegisWAF.Tests;

public sealed class DesktopMultiSiteUpstreamBoundaryContractTests
{
    [Fact]
    public void Desktop_advanced_upstreams_reuse_the_shared_url_boundary_before_request_creation()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "src", "AegisWAF.Desktop", "Views", "SitesView.xaml.cs"),
            Encoding.UTF8);

        var start = source.IndexOf("private bool TryParseAdvancedSettings", StringComparison.Ordinal);
        var end = source.IndexOf("    private sealed class SiteRow", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate Desktop advanced upstream parsing.");
        var parser = source[start..end];

        Assert.Contains("ValidateUpstreamUrl(address)", parser, StringComparison.Ordinal);
        Assert.Contains("Advanced upstream", parser, StringComparison.Ordinal);
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
