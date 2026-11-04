using AegisWAF.Gateway.Sites;
using Npgsql;

namespace AegisWAF.Tests;

public sealed class SiteDatabaseBootstrapFailureTests
{
    [Fact]
    public void TryLoadSnapshot_surfaces_database_connectivity_failures_to_startup()
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "aegiswaf",
            Username = "invalid",
            Password = "invalid",
            Timeout = 1,
            CommandTimeout = 1,
            Pooling = false
        }.ConnectionString;

        Assert.Throws<NpgsqlException>(() => SiteDatabaseStore.TryLoadSnapshot(connectionString));
    }
}
