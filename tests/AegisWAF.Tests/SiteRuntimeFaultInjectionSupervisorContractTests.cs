namespace AegisWAF.Tests;

public sealed class SiteRuntimeFaultInjectionSupervisorContractTests
{
    [Fact]
    public void Fault_injection_runner_and_supervisor_have_explicit_safety_contracts()
    {
        var root = FindRepoRoot();
        var runnerProject = File.ReadAllText(Path.Combine(root, "tools", "fault-injection", "SiteRuntimeFaultInjectionRunner.csproj"));
        var runner = File.ReadAllText(Path.Combine(root, "tools", "fault-injection", "Program.cs"));
        var supervisor = File.ReadAllText(Path.Combine(root, "tools", "fault-injection", "Invoke-SiteRuntimeFaultInjection.ps1"));
        var readme = File.ReadAllText(Path.Combine(root, "tools", "fault-injection", "README.md"));

        Assert.Contains("Microsoft.NET.Sdk", runnerProject, StringComparison.Ordinal);
        Assert.Contains("AEGISWAF_ENABLE_PG_FAULT_INJECTION", runner, StringComparison.Ordinal);
        Assert.Contains("stop-sentinel", runner, StringComparison.Ordinal);
        Assert.Contains("EmbeddedGateway", runner, StringComparison.Ordinal);
        Assert.Contains("/ready", supervisor, StringComparison.Ordinal);
        Assert.Contains("ManagePostgres", supervisor, StringComparison.Ordinal);
        Assert.Contains("finally", supervisor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disposable", readme, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
