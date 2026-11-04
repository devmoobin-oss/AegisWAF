using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteMutationValidationReadBudgetTests
{
    [Fact]
    public void Site_create_and_update_validation_reads_use_a_bounded_budget()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "AegisWAF.Gateway", "GatewayApp.cs"), Encoding.UTF8);

        var createEndpoint = Endpoint(source, "app.MapPost(\"/api/sites\"", "app.MapPut(\"/api/sites/{id}\"");
        var updateEndpoint = Endpoint(source, "app.MapPut(\"/api/sites/{id}\"", "app.MapDelete(\"/api/sites/{id}\"");

        foreach (var endpoint in new[] { createEndpoint, updateEndpoint })
        {
            Assert.Contains("using var validationBudget = SiteMutationReadBudget.Create(ct);", endpoint, StringComparison.Ordinal);
            Assert.Contains("var validationCt = validationBudget.Token;", endpoint, StringComparison.Ordinal);
            Assert.Contains("store.ListAsync(validationCt)", endpoint, StringComparison.Ordinal);
            Assert.Contains("SiteMutationReadBudget.IsBudgetCancellation(ct, validationCt)", endpoint, StringComparison.Ordinal);
            Assert.Contains("site_mutation_read_timeout", endpoint, StringComparison.Ordinal);
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
