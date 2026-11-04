using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHostnameConflictProblemContractTests
{
    [Fact]
    public void Site_hostname_conflicts_use_a_stable_safe_problem_response()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        Assert.Contains("SiteHostnameConflict", source, StringComparison.Ordinal);
        Assert.Contains("site_hostname_conflict", source, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status409Conflict", source, StringComparison.Ordinal);
        Assert.Contains("title: \"Site hostname conflict\"", source, StringComparison.Ordinal);
        Assert.Contains("one or more hostnames are already assigned to another Site", source, StringComparison.Ordinal);
        Assert.Contains("traceId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("return Results.Conflict(new { error = ex.Message });", source, StringComparison.Ordinal);
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
