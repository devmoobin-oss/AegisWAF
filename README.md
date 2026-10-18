# AegisWAF v0.7 — Authentication & RBAC

Experimental self-hosted WAF / reverse proxy (ASP.NET Core .NET 10, YARP, PostgreSQL). **Not production-ready** — see [docs/SECURITY.md](docs/SECURITY.md).

Docs: [ARCHITECTURE](docs/ARCHITECTURE.md) · [SECURITY](docs/SECURITY.md) · [ROADMAP](docs/DEVELOPMENT_ROADMAP.md) · [PROGRESS](docs/PROGRESS.md)

## Windows desktop app (WPF)
`dist\AegisWAF-Desktop\AegisWAF.exe` is a native Windows application (C# / WPF, self-contained — no .NET install needed). **The gateway is built in**: it runs inside the app's own process (no second exe, no terminal window) and stops when the app closes.

### First run — a 4-step wizard
1. **ฐานข้อมูล** — host / port / user / password / database name as separate fields (no connection string to write). *ทดสอบการเชื่อมต่อ* tells you in plain language what is wrong (PostgreSQL not running, wrong password, database missing) and offers *สร้างฐานข้อมูลให้* to create it.
2. **ผู้ดูแลระบบ** — choose the first administrator's username/password (any password; only "the two fields match" is checked; skipped if the database already has users).
3. **เว็บไซต์ที่ปกป้อง** — the real site's address (with a *ตรวจสอบ* reachability check) and the port users will go through.
4. **เริ่มใช้งาน** — summary, then it starts the protection and signs you in automatically.

Settings are stored in `%AppData%\AegisWAF\gateway.settings`, encrypted with Windows DPAPI for your Windows account only. The administrator's password is never stored.

### Every later run
The login window has only username + password; the protection starts in the background (status shown at the top). Links: *ใช้ Gateway ที่รันอยู่เครื่องอื่น* (connect to a standalone gateway) and *ตั้งค่าใหม่*.

### Using it
* **ภาพรวม** — a banner shows what is protected and the address users must visit (copy / open buttons), plus **ทดสอบระบบป้องกัน**, which sends one harmless simulated attack and tells you whether it was blocked. An empty dashboard explains what to do next.
* **เหตุการณ์ที่ถูกบล็อก** — each event is explained in plain Thai; **บล็อก IP นี้** blocks the attacker in one click (with a warning if it is your own machine).
* **กฎการป้องกัน**, **IP ที่บล็อก / อนุญาต**, **ผู้ใช้งานระบบ** (SUPER_ADMIN), **ประวัติการใช้งาน**, and **ตั้งค่า** (change database / site / port later, or reset everything). Every page has a one-line explanation under its title. Controls are disabled according to your role; the server enforces permissions regardless.

The in-app gateway listens on `127.0.0.1` only (admin API and proxy share that port). Build with `dotnet publish src/AegisWAF.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` (about 160 MB).

The standalone gateway and browser console below still work and are unchanged.

## What the system protects against
Signature + normalisation filter (v0.7.1). Before matching, each field is percent-decoded (including double-encoding), `+`→space, HTML-entity-decoded, SQL-comment-stripped and lowercased, so URL-borne attacks that browsers encode as `%20`/`%3C` are now caught. It inspects the URL, query, **headers, cookies and bodies of every textual type** (JSON, form, text, XML, multipart, chunked) against 13 rule groups — **partial** coverage for SQL injection, XSS, path traversal, LFI/RFI, command injection, SSRF and header attacks; plus IP block/allow and a 300 req/min/IP limit. Not covered: response inspection, NoSQL/XXE, full bot management, DDoS. Tuned for maximum detection, so expect some false positives — every rule is individually toggleable.
The full, honest list with real examples is in [docs/PROTECTION.md](docs/PROTECTION.md) and in the app under **ระบบป้องกันอะไรได้บ้าง** (with "ลองทดสอบ" buttons that run each example on your own gateway). Every example is executed against a real gateway by `CatalogVerificationTests`, so the list cannot drift from the code.

## Running AegisWAF.Gateway.exe by double-click
The gateway needs a PostgreSQL connection **with the password**; the shipped `appsettings.json` deliberately has none. If it is missing the program prints why and waits for Enter (it used to close silently). Either set the `ConnectionStrings__Postgres` environment variable, or copy `appsettings.local.json.example` to `appsettings.local.json` beside the exe and fill it in (the `Bootstrap` section creates the first admin when no users exist — delete the password line after first login). Environment variables override the file. Keep `appsettings.local.json` private: it contains a database password in clear text.

## Requirements
- .NET 10 SDK, PostgreSQL 18, protected backend on `http://localhost:8080/` (see `ReverseProxy` in `appsettings.json`).

## Setup (PowerShell)
```powershell
# 1. Create the database once (migrations run automatically at startup and are tracked in schema_migrations)
psql -U postgres -c "CREATE DATABASE aegiswaf;"

# 2. Secrets come from the environment, never from source control
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=aegiswaf;Username=postgres;Password=<your password>;Pooling=true'
$env:AEGISWAF_BOOTSTRAP_ADMIN_PASSWORD = '<any password>'   # only used when no users exist yet
# optional: $env:AEGISWAF_BOOTSTRAP_ADMIN_USER = 'admin'

# 3. Run from the project directory (content root must contain wwwroot)
cd src/AegisWAF.Gateway
dotnet run --urls http://127.0.0.1:5080
```
Open `http://127.0.0.1:5080` and sign in as the bootstrap user. Change the password afterwards (click your name in the header). The dashboard and `/api` are reachable from the local machine only (`Admin:AllowRemote=false`).

Set `Database:AutoMigrate=false` to apply `database/*.sql` yourself; a modified, already-applied script is rejected by checksum.

## Roles
`VIEWER` < `SECURITY_ANALYST` < `SECURITY_OPERATOR` < `SECURITY_ADMIN` < `SUPER_ADMIN`. Users are managed under *ผู้ใช้และสิทธิ์* (SUPER_ADMIN only). Accounts created by an administrator must set their own password at first login. Permission matrix: [ARCHITECTURE.md](docs/ARCHITECTURE.md#authorization-matrix).

## Configuration
| Key | Default | Meaning |
|---|---|---|
| `Auth:SessionIdleMinutes` | 30 | idle session timeout |
| `Auth:SessionAbsoluteHours` | 8 | maximum session age |
| `Auth:MaxFailedAttempts` / `Auth:LockoutMinutes` | 5 / 15 | account lockout |
| `Admin:AllowRemote` | false | allow non-loopback clients to reach the GUI/API (only behind HTTPS) |
| `Database:AutoMigrate` | true | run embedded migrations at startup |

## Tests
```powershell
dotnet test      # needs PostgreSQL on localhost with postgres/postgres, or set AEGISWAF_TEST_PG
```
Each run creates and drops its own throw-away database. `AEGISWAF_TEST_PG` format: `Host=...;Port=5432;Username=...;Password=...`.

## Breaking changes from v0.6
- `AEGISWAF_ADMIN_TOKEN` and the `X-Aegis-Admin-Token` header are **removed**; use login sessions.
- Unknown `/api/*` paths are no longer forwarded to the upstream.

## OWASP defense coverage

The admin console includes an A01-A10 coverage view and scoped defense metadata. See [docs/OWASP_DEFENSE.md](docs/OWASP_DEFENSE.md). OWASP badges are coverage mapping, not certification.

## Still limited (selected)
Dashboard and admin API share the proxy listener, so protected apps cannot use `/`, `/assets` or `/api` paths; rate limiting and counters are per-process; no TLS management, MFA, or load testing. The detection engine (v0.9) normalises each field (decode / HTML-entity / `/**/` strip / NFKC Unicode fold), runs 16 signature groups + an optional anomaly scorer across URL, headers, cookies and all request bodies, and inspects upstream responses for leaks — but it is a signature/normalisation layer, not a full WAF, with documented bypasses and false positives (see [docs/PROTECTION.md](docs/PROTECTION.md)) and no independent pen-test or OWASP CRS benchmark ([docs/OWASP_CRS_COMPATIBILITY.md](docs/OWASP_CRS_COMPATIBILITY.md)). Full list: [docs/SECURITY.md](docs/SECURITY.md).
