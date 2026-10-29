using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteMutationSerializationContractTests
{
    [Fact]
    public void Site_mutations_use_one_cross_instance_advisory_lock_before_validation_and_activation()
    {
        var root = FindRepoRoot();
        var store = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);
        var gateway = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        Assert.Contains("AcquireSiteMutationLockAsync", store, StringComparison.Ordinal);
        Assert.Contains("pg_try_advisory_lock", store, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT pg_advisory_lock", store, StringComparison.Ordinal);
        Assert.Contains("aegiswaf.site_mutation.v1", store, StringComparison.Ordinal);
        Assert.Equal(4, Count(gateway, "AcquireSiteMutationLockAsync"));
        Assert.Equal(3, Count(gateway, "await using var mutationLock"));
    }

    private static int Count(string value, string token) =>
        value.Split(token, StringSplitOptions.None).Length - 1;

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
