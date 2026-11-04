using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AegisWAF.Tests;

/// <summary>Creates a throw-away PostgreSQL database, boots the real Gateway against it and drops it afterwards.</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Bootstrap-Test-Passw0rd!";
    private readonly string _dbName = "aegiswaf_test_" + Guid.NewGuid().ToString("N")[..8];
    private string _adminCs = "";
    private WebApplicationFactory<Program> _factory = null!;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var baseCs = Environment.GetEnvironmentVariable("AEGISWAF_TEST_PG") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
        _adminCs = baseCs + ";Database=postgres";
        await Exec(_adminCs, $"CREATE DATABASE {_dbName}");
        ConnectionString = baseCs + $";Database={_dbName};Pooling=true";
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", ConnectionString);
        Environment.SetEnvironmentVariable("AEGISWAF_BOOTSTRAP_ADMIN_USER", AdminUser);
        Environment.SetEnvironmentVariable("AEGISWAF_BOOTSTRAP_ADMIN_PASSWORD", AdminPassword);
        _factory = new Factory();
        _ = _factory.Server; // boot: runs migrations + bootstrap
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
        await Exec(_adminCs, $"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)");
    }

    public Api NewClient() => new(_factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }));

    public WebApplicationFactory<Program> CreatePeer()
    {
        var peer = new Factory();
        _ = peer.Server; // boot a second Gateway process against the same PostgreSQL database
        return peer;
    }

    /// <summary>There is no login: kept so existing call sites that just want "a" client still compile.</summary>
    public Task<Api> LoginAdmin() => Task.FromResult(NewClient());

    public async Task<long> CountAudit(string action, string outcome, string? actor = null)
    {
        await using var db = new NpgsqlConnection(ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM waf_audit WHERE action=@a AND outcome=@o AND (@u::text IS NULL OR actor=@u)", db);
        cmd.Parameters.AddWithValue("a", action); cmd.Parameters.AddWithValue("o", outcome);
        cmd.Parameters.AddWithValue("u", (object?)actor ?? DBNull.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task Exec(string cs, string sql)
    {
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, db);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(s => s.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, RemoteIpFilter>());
        }
    }

    /// <summary>Lets a test simulate a non-local client via the X-Test-Remote header (TestServer has no real socket).</summary>
    private sealed class RemoteIpFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
        {
            app.Use(async (HttpContext ctx, Func<Task> n) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-Remote", out var v) && IPAddress.TryParse(v.ToString(), out var ip)) ctx.Connection.RemoteIpAddress = ip;
                await n();
            });
            next(app);
        };
    }
}

public sealed class Api(HttpClient http)
{
    public string? Csrf { get; private set; }
    public HttpClient Http => http;

    public async Task<HttpResponseMessage> Login(string user, string password)
    {
        var r = await http.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        await r.Content.LoadIntoBufferAsync(); // tests re-read the body
        if (r.IsSuccessStatusCode) Csrf = (await Json(r)).GetProperty("csrfToken").GetString();
        return r;
    }

    public Task<HttpResponseMessage> Get(string url) => http.GetAsync(url);

    public Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, bool csrf = true, string? ifMatch = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (csrf && Csrf is not null) req.Headers.Add("X-CSRF-Token", Csrf);
        if (!string.IsNullOrWhiteSpace(ifMatch)) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return http.SendAsync(req);
    }

    public async Task<JsonElement> Json(HttpResponseMessage r)
    {
        await r.Content.LoadIntoBufferAsync();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}

[CollectionDefinition("gateway")]
public sealed class GatewayCollection : ICollectionFixture<GatewayFixture> { }
