using Npgsql;
using System.Text.Json;
using AegisWAF.Gateway.Sites;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace AegisWAF.Tests;

[Collection("gateway")]
public sealed class SiteDatabaseCrudTests(GatewayFixture fx)
{
    [Fact]
    public async Task Disposing_a_mutation_lock_releases_it_for_the_next_pooled_connection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var firstStore = new SiteDatabaseStore(config);
        var secondStore = new SiteDatabaseStore(config);
        await using var first = await firstStore.AcquireSiteMutationLockAsync(default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var secondTask = secondStore.AcquireSiteMutationLockAsync(cancellation.Token);
        try
        {
            await Task.Delay(100);
            await first.DisposeAsync();
            await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            cancellation.Cancel();
            try { await secondTask; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Direct_store_reads_accept_canonicalized_site_id_input()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var id = "boundary-" + Guid.NewGuid().ToString("N")[..6];
        var site = new GatewaySite(id, true, [id + ".example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));
        Assert.True(await store.InsertAsync(site, "test", default));
        try
        {
            var restored = await store.FindByIdAsync($"  {id.ToUpperInvariant()}  ", default);
            Assert.NotNull(restored);
            Assert.Equal(id, restored.Id);

            await store.RecordHealthHistoryAsync($"  {id.ToUpperInvariant()}  ",
                [new SiteHealthObservation("primary", "healthy", null, DateTimeOffset.UtcNow)], default);
            var history = await store.ListHealthHistoryAsync($"  {id.ToUpperInvariant()}  ", 10, default);
            Assert.Single(history);
        }
        finally
        {
            await store.DeleteAsync($"  {id.ToUpperInvariant()}  ", default);
        }
    }

    [Fact]
    public async Task Concurrent_active_hostname_reservation_allows_only_one_site()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var first = new GatewaySite(
            "host-a-" + suffix, true, ["shared-" + suffix + ".example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin-a.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin-a.example.test"], [443], ["https"]));
        var second = first with
        {
            Id = "host-b-" + suffix,
            Upstreams = [new SiteUpstream("primary", new Uri("https://origin-b.example.test/"))],
            Egress = new SiteEgressPolicy(["origin-b.example.test"], [443], ["https"])
        };

        var results = await Task.WhenAll(
            CaptureInsert(store, first),
            CaptureInsert(store, second));

        Assert.Single(results, result => result);
        await store.DeleteAsync(first.Id, default);
        await store.DeleteAsync(second.Id, default);

        static async Task<bool> CaptureInsert(SiteDatabaseStore store, GatewaySite site)
        {
            try { return await store.InsertAsync(site, "test", "127.0.0.1", default); }
            catch (SiteHostnameConflictException) { return false; }
        }
    }

    [Fact]
    public async Task Duplicate_active_hostname_returns_actionable_validation_detail()
    {
        var api = await fx.LoginAdmin();
        var firstId = "hostname-owner-" + Guid.NewGuid().ToString("N")[..6];
        var duplicateId = "hostname-duplicate-" + Guid.NewGuid().ToString("N")[..6];
        var hostname = firstId + ".example.test";
        object Payload(string id) => new
        {
            id, hostnames = new[] { hostname }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        };
        try
        {
            var first = await api.Send(HttpMethod.Post, "/api/sites", Payload(firstId));
            first.EnsureSuccessStatusCode();
            var duplicate = await api.Send(HttpMethod.Post, "/api/sites", Payload(duplicateId));
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            var detail = await api.Json(duplicate);
            Assert.Contains("already assigned", detail.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(hostname, detail.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await api.Send(HttpMethod.Delete, "/api/sites/" + firstId, ifMatch: "\"1\"");
            await api.Send(HttpMethod.Delete, "/api/sites/" + duplicateId, ifMatch: "\"1\"");
        }
    }

    [Fact]
    public async Task Site_mutation_writes_an_audit_event_in_the_same_transaction()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var id = "audit-site-" + Guid.NewGuid().ToString("N")[..6];
        var site = new GatewaySite(
            id, true, [id + ".example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));

        Assert.True(await store.InsertAsync(site, "audit-user", "192.0.2.10", default));
        try
        {
            Assert.True(await fx.CountAudit("site.create", "success", "audit-user") >= 1);
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Site_mutations_emit_redacted_notifications_after_commit()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var id = "notify-site-" + Guid.NewGuid().ToString("N")[..6];
        var site = new GatewaySite(
            id, true, [id + ".example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));
        await using var listener = new NpgsqlConnection(fx.ConnectionString);
        await listener.OpenAsync();
        await using (var listen = new NpgsqlCommand($"LISTEN {SiteRuntimeChangeNotification.Channel};", listener))
            await listen.ExecuteNonQueryAsync();

        try
        {
            Assert.True(await store.InsertAsync(site, "notify-test", default));
            var created = await WaitForNotificationAsync(listener);
            AssertNotification(created, "site.create", id);

            var updated = await store.UpdateWithConcurrencyAsync(site with { WafPolicy = "strict" }, 1, "notify-test", null, default);
            Assert.Equal(SiteUpdateStatus.Updated, updated.Status);
            var changed = await WaitForNotificationAsync(listener);
            AssertNotification(changed, "site.update", id);

            var deleted = await store.DeleteWithConcurrencyAsync(id, updated.CurrentVersion!.Value, "notify-test", null, default);
            Assert.Equal(SiteDeleteStatus.Deleted, deleted.Status);
            var removed = await WaitForNotificationAsync(listener);
            AssertNotification(removed, "site.delete", id);
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }

        static async Task<string> WaitForNotificationAsync(NpgsqlConnection listener)
        {
            var payload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNotification(object? _, NpgsqlNotificationEventArgs args) => payload.TrySetResult(args.Payload);
            listener.Notification += OnNotification;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await listener.WaitAsync(timeout.Token);
                return await payload.Task.WaitAsync(timeout.Token);
            }
            finally { listener.Notification -= OnNotification; }
        }

        static void AssertNotification(string payload, string operation, string siteId)
        {
            Assert.True(SiteRuntimeChangeNotification.TryParse(payload, out var change));
            Assert.Equal(operation, change.Operation);
            Assert.Equal(siteId, change.SiteId);
            Assert.DoesNotContain("origin.example.test", payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Database_round_trip_preserves_custom_health_and_rate_limit_policy()
    {
        var id = "policy-" + Guid.NewGuid().ToString("N")[..6];
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var site = new GatewaySite(
            id, true, [id + ".example.test"], ["/api"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/base/"), 700)],
            "default", new SiteRateLimitPolicy(1200, 90, 40000),
            new SiteEgressPolicy(["origin.example.test"], [443], ["https"], AllowPrivateExceptions: true),
            new SiteHealthPolicy("/ready", 30, 5, 120));

        Assert.True(await store.InsertAsync(site, "test", default));
        try
        {
            var restored = Assert.Single(await store.ListAsync(default), x => x.Id == id);
            Assert.True(SiteConfigurationEditor.AreEquivalent(site, restored));
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Health_check_endpoint_returns_the_current_site_status()
    {
        var api = await fx.LoginAdmin();
        var id = "health-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { "health.example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        var response = await api.Send(HttpMethod.Post, "/api/sites/" + id + "/health");
        response.EnsureSuccessStatusCode();
        var body = await api.Json(response);
        Assert.Equal(id, body.GetProperty("siteId").GetString());
        Assert.True(body.TryGetProperty("health", out _));

        var history = await api.Get("/api/sites/" + id + "/health/history?limit=1");
        history.EnsureSuccessStatusCode();
        var historyBody = await api.Json(history);
        Assert.Equal("postgresql", historyBody.GetProperty("retention").GetString());
        Assert.True(historyBody.GetProperty("observations").GetArrayLength() <= 1);
        Assert.DoesNotContain("address", historyBody.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_history_is_persisted_and_trimmed_to_the_per_site_bound()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        var id = "history-" + Guid.NewGuid().ToString("N")[..6];
        var site = new GatewaySite(id, true, [id + ".example.test"], ["/"],
            [new SiteUpstream("primary", new Uri("https://origin.example.test/"))], "default",
            new SiteRateLimitPolicy(), new SiteEgressPolicy(["origin.example.test"], [443], ["https"]));
        Assert.True(await store.InsertAsync(site, "test", default));
        try
        {
            var start = DateTimeOffset.UtcNow;
            var observations = Enumerable.Range(0, 65)
                .Select(i => new SiteHealthObservation("primary", i % 2 == 0 ? "healthy" : "unhealthy", i % 2 == 0 ? null : "timeout", start.AddSeconds(i)))
                .ToArray();
            await store.RecordHealthHistoryAsync(id, observations, default);

            var restored = await store.ListHealthHistoryAsync(id, 100, default);
            Assert.Equal(64, restored.Count);
            Assert.True((start.AddSeconds(64) - restored[0].CheckedAt).Duration() <= TimeSpan.FromMicroseconds(1));
            Assert.True((start.AddSeconds(1) - restored[^1].CheckedAt).Duration() <= TimeSpan.FromMicroseconds(1));
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Health_trend_endpoint_reports_a_critical_alert_for_repeated_unhealthy_probes()
    {
        var api = await fx.LoginAdmin();
        var id = "trend-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build();
        var store = new SiteDatabaseStore(config);
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.RecordHealthHistoryAsync(id, Enumerable.Range(0, 3)
                .Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(i)))
                .ToArray(), default);

            var response = await api.Get("/api/sites/" + id + "/health/trend?window=3");
            response.EnsureSuccessStatusCode();
            var body = await api.Json(response);
            Assert.Equal("critical", body.GetProperty("alertState").GetString());
            Assert.Equal(3, body.GetProperty("total").GetInt32());
            Assert.Equal(3, body.GetProperty("consecutiveUnhealthy").GetInt32());
            Assert.DoesNotContain("address", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Health_alert_acknowledgement_is_idempotent_and_audited()
    {
        var api = await fx.LoginAdmin();
        var id = "ack-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        var store = new SiteDatabaseStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build());
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.RecordHealthHistoryAsync(id, Enumerable.Range(0, 3)
                .Select(i => new SiteHealthObservation("primary", "unhealthy", "timeout", now.AddSeconds(i)))
                .ToArray(), default);

            (await api.Send(HttpMethod.Post, "/api/sites/" + id + "/health/alerts/acknowledge", new { cooldownSeconds = 300 })).EnsureSuccessStatusCode();
            (await api.Send(HttpMethod.Post, "/api/sites/" + id + "/health/alerts/acknowledge", new { cooldownSeconds = 300 })).EnsureSuccessStatusCode();

            var persisted = await store.GetHealthAlertAcknowledgementAsync(id, "critical", default);
            Assert.NotNull(persisted);
            var trend = await api.Get("/api/sites/" + id + "/health/trend?window=3");
            var body = await api.Json(trend);
            Assert.True(body.GetProperty("alertSuppressed").GetBoolean());
            Assert.Equal("unknown", body.GetProperty("acknowledgedBy").GetString());
            Assert.True(await fx.CountAudit("site.health.alert.acknowledge", "success", "unknown") >= 2);
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Health_alert_acknowledgement_rejects_a_non_actionable_state()
    {
        var api = await fx.LoginAdmin();
        var id = "ack-empty-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        try
        {
            var response = await api.Send(HttpMethod.Post, "/api/sites/" + id + "/health/alerts/acknowledge", new { cooldownSeconds = 300 });
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var body = await api.Json(response);
            Assert.Equal("health_alert_not_actionable", body.GetProperty("error").GetString());
            Assert.Equal("no-data", body.GetProperty("alertState").GetString());
        }
        finally
        {
            var cleanup = await fx.LoginAdmin();
            await cleanup.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: "\"1\"");
        }
    }

    [Fact]
    public async Task Health_summary_endpoint_is_bounded_and_does_not_expose_probe_details()
    {
        var api = await fx.LoginAdmin();
        var id = "summary-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        var store = new SiteDatabaseStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.ConnectionString
        }).Build());
        try
        {
            var now = DateTimeOffset.UtcNow;
            await store.RecordHealthHistoryAsync(id, Enumerable.Range(0, 3)
                .Select(i => new SiteHealthObservation("primary", "unhealthy", "secret-internal-reason", now.AddSeconds(i)))
                .ToArray(), default);
            var response = await api.Get("/api/sites/health/summary?window=3&limit=100");
            response.EnsureSuccessStatusCode();
            var body = await api.Json(response);
            Assert.InRange(body.GetProperty("totalSites").GetInt32(), 1, 100);
            Assert.InRange(body.GetProperty("sites").GetArrayLength(), 1, 100);
            var target = body.GetProperty("sites").EnumerateArray().Single(x => x.GetProperty("siteId").GetString() == id);
            Assert.Equal("critical", target.GetProperty("alertState").GetString());
            Assert.True(body.GetProperty("criticalSites").GetInt32() >= 1);
            Assert.True(body.GetProperty("generatedAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
            Assert.InRange(body.GetProperty("queryDurationMs").GetInt32(), 0, 60_000);
            Assert.Equal(2_000, body.GetProperty("queryBudgetMs").GetInt32());
            Assert.True(body.GetProperty("withinBudget").ValueKind is JsonValueKind.True or JsonValueKind.False);
            Assert.Contains(body.GetProperty("status").GetString(), new[] { "ready", "slow" });
            Assert.InRange(body.GetProperty("summaryFailureCount").GetInt64(), 0, long.MaxValue);
            Assert.Equal(0, body.GetProperty("summaryConsecutiveFailures").GetInt64());
            Assert.DoesNotContain("secret-internal-reason", body.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain("address", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await store.DeleteAsync(id, default);
        }
    }

    [Fact]
    public async Task Health_summary_cleans_expired_acknowledgements_and_clamps_bounds()
    {
        var api = await fx.LoginAdmin();
        var id = "expiry-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        try
        {
            await using var db = new NpgsqlConnection(fx.ConnectionString);
            await db.OpenAsync();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO waf_site_health_alerts(site_id, alert_state, acknowledged_at, acknowledged_until, acknowledged_by)
                VALUES (@site_id, 'critical', now() - interval '10 minutes', now() - interval '1 minute', 'expiry-test')
                """, db);
            insert.Parameters.AddWithValue("site_id", id);
            await insert.ExecuteNonQueryAsync();

            var response = await api.Get("/api/sites/health/summary?window=999&limit=999");
            response.EnsureSuccessStatusCode();
            var body = await api.Json(response);
            Assert.Equal(64, body.GetProperty("windowSize").GetInt32());
            Assert.InRange(body.GetProperty("totalSites").GetInt32(), 1, 100);
            Assert.InRange(body.GetProperty("sites").GetArrayLength(), 1, 100);
            Assert.True(body.GetProperty("expiredAcknowledgementsRemoved").GetInt32() >= 1);
        }
        finally
        {
            var cleanup = await fx.LoginAdmin();
            await cleanup.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: "\"1\"");
        }
    }

    [Fact]
    public async Task Site_update_rejects_a_stale_version_after_another_editor_saves()
    {
        var api = await fx.LoginAdmin();
        var id = "versioned-" + Guid.NewGuid().ToString("N")[..6];
        var add = await api.Send(HttpMethod.Post, "/api/sites", new
        {
            id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true
        });
        add.EnsureSuccessStatusCode();
        try
        {
            var list = await api.Get("/api/sites");
            var current = (await api.Json(list)).EnumerateArray().Single(x => x.GetProperty("siteId").GetString() == id);
            var version = current.GetProperty("version").GetInt64();
            Assert.Equal("active", current.GetProperty("activationStatus").GetString());
            Assert.Equal(version, current.GetProperty("activeVersion").GetInt64());
            var body = new
            {
                id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/api" },
                upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
                allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
                allowedSchemes = new[] { "https" }, enabled = true, expectedVersion = version
            };

            var first = await api.Send(HttpMethod.Put, "/api/sites/" + id, body, ifMatch: $"\"{version}\"");
            first.EnsureSuccessStatusCode();
            Assert.Equal($"\"{version + 1}\"", first.Headers.ETag?.Tag);
            var firstBody = await api.Json(first);
            Assert.True(firstBody.GetProperty("active").GetBoolean());
            Assert.False(firstBody.GetProperty("requiresRestart").GetBoolean());
            var stale = await api.Send(HttpMethod.Put, "/api/sites/" + id, body);

            Assert.True(stale.StatusCode == HttpStatusCode.Conflict,
                await stale.Content.ReadAsStringAsync());
            var conflict = await api.Json(stale);
            Assert.Equal("site_version_conflict", conflict.GetProperty("code").GetString());
            Assert.True(conflict.GetProperty("currentVersion").GetInt64() > version);

            var staleDelete = await api.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: $"\"{version}\"");
            Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
            var deleteConflict = await api.Json(staleDelete);
            Assert.Equal("site_version_conflict", deleteConflict.GetProperty("code").GetString());

            var currentDelete = await api.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: $"\"{version + 1}\"");
            currentDelete.EnsureSuccessStatusCode();
        }
        finally
        {
            await api.Send(HttpMethod.Delete, "/api/sites/" + id, ifMatch: "\"1\"");
        }
    }

    [Fact]
    public async Task Adding_two_sites_persists_both_rows_without_overwriting_the_first()
    {
        var api = await fx.LoginAdmin();
        var firstId = "shop-" + Guid.NewGuid().ToString("N")[..6];
        var secondId = "blog-" + Guid.NewGuid().ToString("N")[..6];

        foreach (var site in new[]
        {
            new { id = firstId, hostnames = new[] { "shop.example.test" } },
            new { id = secondId, hostnames = new[] { "blog.example.test" } }
        })
        {
            var response = await api.Send(HttpMethod.Post, "/api/sites", new
            {
                id = site.id,
                site.hostnames,
                pathPrefixes = new[] { "/" },
                upstreamAddress = "https://origin.example.test/",
                wafPolicy = "default",
                allowedHosts = new[] { "origin.example.test" },
                allowedPorts = new[] { 443 },
                allowedSchemes = new[] { "https" },
                enabled = true
            });
            response.EnsureSuccessStatusCode();
        }

        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT site_id FROM waf_sites WHERE site_id = ANY(@ids) ORDER BY site_id", db);
        cmd.Parameters.AddWithValue("ids", new[] { firstId, secondId });
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<string>();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));

        Assert.Equal(new[] { firstId, secondId }.OrderBy(x => x), ids);
    }

    [Fact]
    public async Task Update_and_delete_change_only_the_selected_database_row()
    {
        var api = await fx.LoginAdmin();
        var firstId = "edit-" + Guid.NewGuid().ToString("N")[..6];
        var secondId = "keep-" + Guid.NewGuid().ToString("N")[..6];
        async Task Add(string id)
        {
            var response = await api.Send(HttpMethod.Post, "/api/sites", new
            {
                id, hostnames = new[] { id + ".example.test" }, pathPrefixes = new[] { "/" },
                upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
                allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
                allowedSchemes = new[] { "https" }, enabled = true
            });
            response.EnsureSuccessStatusCode();
        }
        await Add(firstId);
        await Add(secondId);

        var update = await api.Send(HttpMethod.Put, "/api/sites/" + firstId, new
        {
            id = firstId, hostnames = new[] { "changed.example.test" }, pathPrefixes = new[] { "/api" },
            upstreamAddress = "https://origin.example.test/", wafPolicy = "default",
            allowedHosts = new[] { "origin.example.test" }, allowedPorts = new[] { 443 },
            allowedSchemes = new[] { "https" }, enabled = true, expectedVersion = 1L
        });
        update.EnsureSuccessStatusCode();

        var delete = await api.Send(HttpMethod.Delete, "/api/sites/" + firstId, ifMatch: "\"2\"");
        Assert.True(delete.IsSuccessStatusCode, await delete.Content.ReadAsStringAsync());

        await using var db = new NpgsqlConnection(fx.ConnectionString);
        await db.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT site_id, hostnames FROM waf_sites WHERE site_id=@id OR site_id=@other ORDER BY site_id", db);
        cmd.Parameters.AddWithValue("id", firstId); cmd.Parameters.AddWithValue("other", secondId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string Id, string[] Hosts)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), (string[])reader[1]));

        var kept = Assert.Single(rows);
        Assert.Equal(secondId, kept.Id);
    }
}
