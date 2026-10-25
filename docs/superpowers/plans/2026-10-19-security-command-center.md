# Security Command Center Implementation Plan

> **Execution note:** Execute this plan task-by-task with TDD. Every production change must follow a failing test first, then the smallest implementation, then focused and regression verification.

## Goal

เพิ่มเมนู Security Command Center ใน AegisWAF Desktop และเพิ่ม Gateway telemetry/API เพื่อแสดง traffic ล่าสุด, client, site, upstream, decision, status, latency และเหตุผลด้านความปลอดภัย โดยไม่เก็บข้อมูลลับและไม่ทำให้ request path ขึ้นกับ telemetry storage

## Design constraints

- ใช้ bounded in-memory ring buffer; ไม่เขียน allowed request ทุกตัวลง PostgreSQL
- ใช้ session/RBAC เดิม; detailed traffic ต้องใช้ `SECURITY_ANALYST` ขึ้นไป
- ไม่ส่ง body, response body, Cookie, Authorization, raw headers หรือ raw query string
- จำกัด API response สูงสุด 500 records และใช้ `Cache-Control: no-store`
- ใช้ polling ทุก 2 วินาทีใน Desktop; ไม่เพิ่ม SSE/WebSocket ในงานนี้
- รักษา existing user changes และแก้เฉพาะไฟล์ตาม task

## Files to create/modify

- Create: `src/AegisWAF.Gateway/Traffic/TrafficTelemetry.cs`
- Create: `src/AegisWAF.Gateway/Traffic/TrafficTelemetryModels.cs`
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs` — register telemetry, record completed/short-circuited protected requests, map command-center endpoints
- Modify: `src/AegisWAF.Gateway/Program.cs` — expose telemetry service if the existing top-level registrations require it
- Modify: `src/AegisWAF.Gateway/appsettings.json` — add `Waf:Telemetry:LiveCapacity` default 500
- Modify: `src/AegisWAF.Client/Models.cs` — summary, query and traffic DTOs
- Modify: `src/AegisWAF.Client/ApiClient.cs` — typed command-center methods
- Create: `src/AegisWAF.Desktop/Views/CommandCenterView.xaml`
- Create: `src/AegisWAF.Desktop/Views/CommandCenterView.xaml.cs`
- Modify: `src/AegisWAF.Desktop/MainWindow.xaml` — navigation item
- Modify: `src/AegisWAF.Desktop/MainWindow.xaml.cs` — page registration and keyboard navigation
- Modify: `tests/AegisWAF.Tests/TrafficTelemetryTests.cs`
- Modify: `tests/AegisWAF.Tests/AuthorizationTests.cs` or create a focused command-center authorization test
- Modify: `tests/AegisWAF.Tests/EmbeddedGatewayTests.cs`
- Modify: `tests/AegisWAF.Tests/ApiClientTests.cs`
- Modify: `tests/AegisWAF.Tests/UiContractTests.cs`

## Task 1: Define telemetry model and redaction policy

**Files:**
- Create: `src/AegisWAF.Gateway/Traffic/TrafficTelemetryModels.cs`
- Create: `tests/AegisWAF.Tests/TrafficTelemetryTests.cs`

**Interfaces:**
- `TrafficDecision` enum: `Allowed`, `Blocked`, `RateLimited`, `UpstreamError`, `Unknown`
- `TrafficRecord` record containing timestamp, trace ID, client IP, site ID, hostname, method, sanitized path, upstream ID/address, decision, status, rule, OWASP codes and duration
- `TrafficQuery` record containing capped limit and optional decision/site/IP/text filters
- `TrafficTelemetrySummary` record containing active connections, requests/minute, allowed, blocked, rate limited, upstream errors, buffered and dropped counts
- `TrafficTelemetry.SanitizePath(PathString path, QueryString query)` returns path without query data

- [ ] Write failing tests for query removal, no body/header/token fields, and optional site/upstream values.
- [ ] Run `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter FullyQualifiedName~TrafficTelemetryTests --no-restore`; expect failure because types do not exist.
- [ ] Implement the minimal records and sanitizer with a maximum path length consistent with existing request limits.
- [ ] Run the focused tests and verify they pass.

## Task 2: Implement bounded telemetry store

**Files:**
- Create: `src/AegisWAF.Gateway/Traffic/TrafficTelemetry.cs`
- Modify: `src/AegisWAF.Gateway/appsettings.json`
- Extend: `tests/AegisWAF.Tests/TrafficTelemetryTests.cs`

**Interfaces:**
- `TrafficTelemetry(IConfiguration configuration)` reads `Waf:Telemetry:LiveCapacity`, clamps it to a safe range, and creates a bounded store.
- `BeginRequest()` increments active connections and returns an idempotent scope whose `Dispose()` decrements once.
- `Record(TrafficRecord record)` stores newest-first and increments dropped count when capacity evicts an older item.
- `GetSummary()` returns a snapshot without exposing mutable state.
- `Query(TrafficQuery query)` returns at most the capped limit with server-side filters.

- [ ] Write failing tests for capacity eviction, `limit` cap at 500, dropped count, active count after success/exception/cancellation, and safe non-negative active count.
- [ ] Run the focused tests and verify the expected failures.
- [ ] Implement thread-safe bounded storage using existing .NET primitives; recording must be non-blocking and must not access PostgreSQL.
- [ ] Add default configuration `Waf:Telemetry:LiveCapacity: 500`.
- [ ] Run focused tests and verify all pass.

## Task 3: Capture traffic through the Gateway pipeline

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Modify: `src/AegisWAF.Gateway/Program.cs` only if service registration is located there
- Create/extend: `tests/AegisWAF.Tests/EmbeddedGatewayTests.cs`

**Interfaces:**
- Register `TrafficTelemetry` as a singleton.
- Add one outer protected-request middleware that starts a telemetry scope, calls the remaining pipeline, and records the final response in `finally`.
- Use `SiteContext.Get(ctx)` for site/hostname and the selected site configuration for upstream metadata.
- Set decision using response status and security markers already produced by the WAF pipeline; preserve explicit rule/reason for blocked and rate-limited events.
- Record early 421/400/403/429/502/503 outcomes when the request is short-circuited, without recording management endpoints.

- [ ] Write failing integration tests for an allowed site request containing site/upstream/status/latency and for a blocked request containing rule/OWASP with no raw payload.
- [ ] Run those tests and verify failure because the command-center data is absent.
- [ ] Implement capture with `try/finally`; telemetry failure must be swallowed/logged without changing the client response.
- [ ] Run focused embedded gateway tests and verify pass.
- [ ] Confirm existing counters/security event behavior is unchanged.

## Task 4: Add protected command-center API

**Files:**
- Modify: `src/AegisWAF.Gateway/GatewayApp.cs`
- Create/extend: `tests/AegisWAF.Tests/CommandCenterApiTests.cs`

**Interfaces:**
- `GET /api/command-center/summary` requires `Roles.Analyst` and returns the summary DTO.
- `GET /api/command-center/traffic` requires `Roles.Analyst`; accepts `limit`, `decision`, `siteId`, `ip` and `q`; invalid values return 400, valid values are capped.
- Both endpoints set `Cache-Control: no-store`.
- API responses must be sanitized JSON and must never include request body, cookies, authorization, raw query, connection string or mutable service references.

- [ ] Write failing authorization tests: anonymous and VIEWER receive 401/403; SECURITY_ANALYST receives 200.
- [ ] Write failing endpoint tests for limit cap, decision/site/IP filtering, empty list, no-store header and redaction.
- [ ] Run focused API tests and verify expected failures.
- [ ] Implement endpoints using the singleton telemetry snapshot and existing `AdminGuard`/role conventions.
- [ ] Run focused API tests and verify pass.

## Task 5: Add typed client contracts

**Files:**
- Modify: `src/AegisWAF.Client/Models.cs`
- Modify: `src/AegisWAF.Client/ApiClient.cs`
- Extend: `tests/AegisWAF.Tests/ApiClientTests.cs`

**Interfaces:**
- Add `CommandCenterSummaryDto`.
- Add `CommandCenterTrafficDto` with nullable site/upstream fields and computed display helpers only where safe.
- Add `CommandCenterTrafficQuery` with a safe default limit.
- Add `GetCommandCenterSummaryAsync(CancellationToken)` and `GetCommandCenterTrafficAsync(CommandCenterTrafficQuery, CancellationToken)`.

- [ ] Write failing client tests for summary, traffic records, empty arrays and nullable fields.
- [ ] Run focused client tests and verify failure.
- [ ] Implement URL encoding for filters and use existing `SendAsync` authentication/error handling.
- [ ] Run client tests and verify pass.

## Task 6: Build Desktop command-center view

**Files:**
- Create: `src/AegisWAF.Desktop/Views/CommandCenterView.xaml`
- Create: `src/AegisWAF.Desktop/Views/CommandCenterView.xaml.cs`
- Modify: `src/AegisWAF.Desktop/MainWindow.xaml`
- Modify: `src/AegisWAF.Desktop/MainWindow.xaml.cs`
- Extend: `tests/AegisWAF.Tests/UiContractTests.cs`

**Interfaces:**
- View implements `IPage`, sets `AutoRefresh = true`, and refreshes summary/traffic with the existing 5-second page timer; the view may use a local 2-second timer only if it does not overlap requests.
- Add navigation item and `_pages` entry with a command icon.
- Show summary cards and a sanitized traffic DataGrid with decision/status color styles and OWASP badges.
- Add client-side text filter only for already returned sanitized rows; server-side filters remain the source of truth.
- Show loading, empty, offline and permission-denied states without displaying exception internals.

- [ ] Write failing UI contract assertions for navigation label, view class, summary fields and required table columns.
- [ ] Run UI contract tests and verify failure.
- [ ] Implement XAML/code-behind following existing view patterns and use `Ui.Describe` for safe error messages.
- [ ] Run UI contract tests and verify pass.
- [ ] Run the desktop build to catch XAML/code-behind binding errors.

## Task 7: Regression verification and publish

**Files:**
- No new production files; update docs only if the implementation changes documented behavior.

- [ ] Run focused telemetry, API, client, embedded gateway and UI contract tests.
- [ ] Run the complete test project with `dotnet test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --no-restore` and record any pre-existing failures by name.
- [ ] Run `dotnet build src/AegisWAF.Desktop/AegisWAF.Desktop.csproj -c Release --no-restore`.
- [ ] Publish single-file executable to `D:\AegisWAF\dist\AegisWAF-Desktop` using the project’s existing win-x64 self-contained settings.
- [ ] Verify the output executable exists, has a new hash, and starts without replacing user configuration.
- [ ] Verify endpoint authorization and a real allowed/blocked request through the embedded Gateway before claiming completion.

## Review focus

- A request can be rejected before `SiteContext` exists; it must still appear with safe site/upstream nulls and the correct decision.
- A request can throw, abort or be cancelled; active connections must return to the prior value exactly once.
- A telemetry buffer can be full or its service can fail; the WAF response must remain unchanged.
- A malicious query/body/header may contain secrets or markup; no raw value may cross the API/UI boundary.
- A low-privilege session may call the endpoint directly; server-side RBAC must reject it regardless of Desktop navigation visibility.
- A site can have multiple upstreams or pending restart state; the record must not claim an upstream that was not selected by the active route.

## Verification commands

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' test tests/AegisWAF.Tests/AegisWAF.Tests.csproj --filter 'FullyQualifiedName~TrafficTelemetryTests|FullyQualifiedName~CommandCenterApiTests|FullyQualifiedName~ApiClientTests|FullyQualifiedName~EmbeddedGatewayTests|FullyQualifiedName~UiContractTests' --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' build src/AegisWAF.Desktop/AegisWAF.Desktop.csproj -c Release --no-restore
& 'C:\Program Files\dotnet\dotnet.exe' publish src/AegisWAF.Desktop/AegisWAF.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o 'D:\AegisWAF\dist\AegisWAF-Desktop' --no-restore
```
