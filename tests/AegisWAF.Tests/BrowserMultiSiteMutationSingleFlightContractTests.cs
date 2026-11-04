using System.Text;

namespace AegisWAF.Tests;

public sealed class BrowserMultiSiteMutationSingleFlightContractTests
{
    [Fact]
    public void Browser_multi_site_form_mutation_is_single_flight_even_for_keyboard_submit()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("mutationInFlight=false", sites, StringComparison.Ordinal);
        Assert.Contains("if(mutationInFlight)return", sites, StringComparison.Ordinal);
        Assert.Contains("mutationInFlight=true", sites, StringComparison.Ordinal);
        Assert.Contains("mutationInFlight=false", sites, StringComparison.Ordinal);
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
