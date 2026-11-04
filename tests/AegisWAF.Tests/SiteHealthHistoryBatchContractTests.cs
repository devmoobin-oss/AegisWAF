using System.Text;

namespace AegisWAF.Tests;

public sealed class SiteHealthHistoryBatchContractTests
{
    [Fact]
    public void Health_history_inserts_are_sent_as_one_postgres_batch()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AegisWAF.Gateway", "Sites", "SiteDatabaseStore.cs"), Encoding.UTF8);
        var start = source.IndexOf("public async Task RecordHealthHistoryAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IReadOnlyList<SiteHealthObservation>> ListHealthHistoryAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Could not isolate health-history persistence.");
        var method = source[start..end];

        Assert.Contains("new NpgsqlBatch(db, tx)", method, StringComparison.Ordinal);
        Assert.Contains("new NpgsqlBatchCommand", method, StringComparison.Ordinal);
        Assert.Contains("batch.BatchCommands.Add", method, StringComparison.Ordinal);
        Assert.Contains("await batch.ExecuteNonQueryAsync(ct)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("insert.ExecuteNonQueryAsync", method, StringComparison.Ordinal);
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
