using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteMutationCommittedOutcomeContractTests
{
    [Fact]
    public void Runtime_activation_failures_expose_that_the_database_mutation_committed()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        Assert.Contains("[\"mutationCommitted\"] = true", source, StringComparison.Ordinal);
        Assert.Contains("[\"activationStatus\"] = \"pending-restart\"", source, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeActivationFailed", source, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeActivationTimedOut", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_mutation_flow_distinguishes_a_committed_site_from_a_failed_runtime_activation()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "wwwroot", "assets", "app.js"), Encoding.UTF8);
        var start = source.IndexOf("async function loadSitesCrud", StringComparison.Ordinal);
        var end = source.IndexOf("const loadSectionWithSiteCrud", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate the browser Multi-Site workflow.");
        var sites = source[start..end];

        Assert.Contains("readApiErrorDetails", source, StringComparison.Ordinal);
        Assert.Contains("mutationActivationPending", sites, StringComparison.Ordinal);
        Assert.Contains("problem.mutationCommitted===true", sites, StringComparison.Ordinal);
        Assert.Contains("แต่ยังเปิดใช้งาน route ไม่สำเร็จ", sites, StringComparison.Ordinal);
        Assert.Contains("await load()", sites, StringComparison.Ordinal);
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
