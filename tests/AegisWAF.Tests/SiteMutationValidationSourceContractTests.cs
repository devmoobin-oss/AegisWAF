using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteMutationValidationSourceContractTests
{
    [Fact]
    public void Site_mutation_validation_uses_the_authoritative_database_snapshot()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);
        var post = Endpoint(source, "app.MapPost(\"/api/sites\"", "}).Require(Roles.SecurityAdmin);");
        var putStart = source.IndexOf("app.MapPut(\"/api/sites/{id}\"", StringComparison.Ordinal);
        var putEnd = source.IndexOf("}).Require(Roles.SecurityAdmin);", putStart, StringComparison.Ordinal);
        Assert.True(putStart >= 0 && putEnd > putStart, "Could not isolate the Site update endpoint.");
        var put = source[putStart..putEnd];

        foreach (var endpoint in new[] { post, put })
        {
            Assert.Contains("storedSitesForValidation = await store.ListAsync(ct)", endpoint);
            Assert.Contains("SiteConfigurationValidator.ValidateSnapshot", endpoint);
            Assert.DoesNotContain("registry.Current.Sites", endpoint);
        }
    }

    private static string Endpoint(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate endpoint {startMarker}.");
        return source[start..end];
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
