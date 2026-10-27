using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteRuntimeListenerStatusContractTests
{
    [Fact]
    public void Overview_exposes_redacted_listener_state_for_operator_diagnostics()
    {
        var root = FindRepoRoot();
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var listener = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeChangeListener.cs"), Encoding.UTF8);
        var status = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteRuntimeListenerStatus.cs"), Encoding.UTF8);

        Assert.Contains("SiteRuntimeListenerStatus", gateway, StringComparison.Ordinal);
        Assert.Contains("AddCheck<SiteRuntimeListenerHealthCheck>", gateway, StringComparison.Ordinal);
        Assert.Contains("siteRuntimeListener = listenerStatus.Current", gateway, StringComparison.Ordinal);
        Assert.Contains("SiteRuntimeReloadRetry.RunAsync", listener, StringComparison.Ordinal);
        Assert.Contains("status.MarkReloadFailure(exception)", listener, StringComparison.Ordinal);
        Assert.Contains("BoundedTypeName", status, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", listener + status, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionString", status, StringComparison.OrdinalIgnoreCase);
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
