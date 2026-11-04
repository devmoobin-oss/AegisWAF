using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeActivationProblemContractTests
{
    [Fact]
    public void Site_mutation_activation_failures_do_not_echo_exception_messages()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        Assert.Contains("SiteRuntimeActivationFailed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"detail\"] = ex.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("detail: ex.Message", source, StringComparison.Ordinal);
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
