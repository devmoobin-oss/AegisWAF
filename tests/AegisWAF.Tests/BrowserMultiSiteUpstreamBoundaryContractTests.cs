using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteUpstreamBoundaryContractTests
{
    [Fact]
    public void Browser_upstream_parser_rejects_query_strings_and_fragments_before_posting()
    {
        var root = FindRepoRoot();
        var js = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = js.IndexOf("const parseUpstreams=value=>", StringComparison.Ordinal);
        var end = js.IndexOf("  const healthLabel", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the Browser Upstream parser.");
        var parser = js[start..end];

        Assert.Contains("parsed.search||parsed.hash", parser, StringComparison.Ordinal);
        Assert.Contains("query หรือ fragment", parser, StringComparison.Ordinal);
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
