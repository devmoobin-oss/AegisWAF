using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Npgsql;
using AegisWAF.Gateway;
using AegisWAF.Gateway.Auth;
using AegisWAF.Gateway.Infrastructure;
using AegisWAF.Client;

var builder = GatewayApp.CreateBuilder(args);
GatewayApp.AddServices(builder);
var app = builder.Build();
try { await Bootstrap.RunAsync(app); }
catch (Exception ex)
{
    // Fail closed with a readable message instead of vanishing: without the database nobody can authenticate.
    Console.OutputEncoding = Encoding.UTF8;
    Console.Error.WriteLine("AegisWAF cannot start: database setup failed.");
    Console.Error.WriteLine("  " + (ex is NpgsqlException or InvalidOperationException or System.Net.Sockets.SocketException ? ex.Message : ex.GetType().Name + ": " + ex.Message));
    Console.Error.WriteLine("Check that PostgreSQL is running and that the connection string (with password) is set, either:");
    Console.Error.WriteLine("  - environment variable ConnectionStrings__Postgres, or");
    Console.Error.WriteLine("  - appsettings.local.json next to this program: {\"ConnectionStrings\":{\"Postgres\":\"Host=localhost;Port=5432;Database=aegiswaf;Username=postgres;Password=...;Pooling=true\"}}");
    Console.Error.WriteLine("(ไม่สามารถเริ่มระบบได้: เชื่อมต่อฐานข้อมูลไม่สำเร็จ — ตรวจสอบ PostgreSQL และรหัสผ่านใน connection string)");
    app.Logger.LogCritical(ex, "Startup failed");
    if (Environment.UserInteractive && !Console.IsInputRedirected) { Console.Error.WriteLine(); Console.Error.WriteLine("Press Enter to close."); Console.ReadLine(); }
    Environment.ExitCode = 1;
    return;
}
GatewayApp.MapPipeline(app);
app.Run();

record RuleToggle(bool Enabled);
record RuleTestInput(string? Sample);
record CustomRuleInput(string Name,string Pattern,int Priority,bool Enabled);
record IpPolicyInput(string Ip,string Action,string? Reason);
sealed class IpAccessPolicy
{
    private readonly ConcurrentDictionary<string,(string Action,string Reason)> _items=new();
    public string? Check(string ip) => _items.TryGetValue(ip,out var v)?v.Action:null;
    public void Upsert(string ip,string action,string reason)=>_items[ip]=(action,reason);
    public void Remove(string ip)=>_items.TryRemove(ip,out _);
    public object List()=>_items.OrderBy(x=>x.Key).Select(x=>new {ip=x.Key,action=x.Value.Action,reason=x.Value.Reason});
}
sealed class IpPolicyLoader(IpAccessPolicy policies,IConfiguration cfg,ILogger<IpPolicyLoader> logger):IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try { await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand("SELECT ip,action,reason FROM waf_ip_policies",db);await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))policies.Upsert(reader.GetString(0),reader.GetString(1),reader.GetString(2));
        }catch(Exception ex){logger.LogWarning(ex,"IP policy restoration failed; run 004_security_controls.sql");}
    }
    public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
}
static class Audit
{
    private static byte[] Hash(DateTimeOffset occurred, string action, string target, string? actor, string? ip, string outcome, byte[]? previous)
    {
        var canonical = string.Join("|", occurred.ToUniversalTime().ToString("O"), action, target, actor ?? "", ip ?? "", outcome,
            previous is null ? "" : Convert.ToHexString(previous));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    public static async Task Write(NpgsqlConnection db,NpgsqlTransaction? tx,string action,string target,CancellationToken ct,string? actor=null,string? ip=null,string outcome="success")
    {
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(7002)", db, tx)) await lockCmd.ExecuteNonQueryAsync(ct);
        byte[]? previous = null;
        await using (var previousCmd = new NpgsqlCommand("SELECT entry_hash FROM waf_audit ORDER BY id DESC LIMIT 1", db, tx))
            previous = (await previousCmd.ExecuteScalarAsync(ct)) as byte[];
        var occurred = DateTimeOffset.UtcNow;
        var entry = Hash(occurred, action, target, actor, ip, outcome, previous);
        await using var cmd=new NpgsqlCommand("INSERT INTO waf_audit(occurred_at,action,target,actor,client_ip,outcome,prev_hash,entry_hash) VALUES (@at,@a,@t,@actor,@ip,@o,@prev,@entry)",db,tx);
        cmd.Parameters.AddWithValue("at", occurred);cmd.Parameters.AddWithValue("a",action);cmd.Parameters.AddWithValue("t",target);
        cmd.Parameters.AddWithValue("actor",(object?)actor??DBNull.Value);cmd.Parameters.AddWithValue("ip",(object?)ip??DBNull.Value);cmd.Parameters.AddWithValue("o",outcome);
        cmd.Parameters.AddWithValue("prev",(object?)previous??DBNull.Value);cmd.Parameters.AddWithValue("entry",entry);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<(bool Valid, long Checked, long? BadId)> VerifyAsync(NpgsqlConnection db, CancellationToken ct)
    {
        byte[]? previous = null; long checkedRows = 0;
        await using var cmd = new NpgsqlCommand("SELECT id,occurred_at,action,target,actor,client_ip,outcome,prev_hash,entry_hash FROM waf_audit ORDER BY id", db);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entry = reader.IsDBNull(8) ? null : (byte[])reader[8];
            if (entry is null) { previous = null; continue; } // legacy rows before integrity chaining
            var id = reader.GetInt64(0);
            var occurred = reader.GetFieldValue<DateTimeOffset>(1);
            var action = reader.GetString(2); var target = reader.GetString(3);
            var actor = reader.IsDBNull(4) ? null : reader.GetString(4);
            var ip = reader.IsDBNull(5) ? null : reader.GetString(5); var outcome = reader.GetString(6);
            var recordedPrevious = reader.IsDBNull(7) ? null : (byte[])reader[7];
            if (!((recordedPrevious is null && previous is null) || (recordedPrevious is not null && previous is not null && recordedPrevious.SequenceEqual(previous))))
                return (false, checkedRows, id);
            var expected = Hash(occurred, action, target, actor, ip, outcome, recordedPrevious);
            checkedRows++;
            if (!CryptographicOperations.FixedTimeEquals(expected, entry)) return (false, checkedRows, id);
            previous = entry;
        }
        return (true, checkedRows, null);
    }
}

static class Bootstrap
{
    /// <summary>Applies migrations and creates the first SUPER_ADMIN from environment variables when no users exist.</summary>
    public static async Task RunAsync(WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
        var cs = app.Configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs)) { log.LogWarning("No PostgreSQL connection configured; administration is unavailable"); return; }
        if (app.Configuration.GetValue("Database:AutoMigrate", true))
            foreach (var v in await Migrator.ApplyAsync(cs)) log.LogInformation("Applied migration {Version}", v);
        if (app.Configuration.GetValue("Database:RequireLeastPrivilege", false))
        {
            await using var securityDb = new NpgsqlConnection(cs);
            await securityDb.OpenAsync();
            await using var securityCmd = new NpgsqlCommand("SELECT usesuper FROM pg_user WHERE usename=current_user", securityDb);
            if (Convert.ToBoolean(await securityCmd.ExecuteScalarAsync()))
                throw new InvalidOperationException("Production policy rejected a PostgreSQL superuser account; configure a least-privilege database role.");
        }
        var store = app.Services.GetRequiredService<AuthStore>();
        if (await store.CountUsersAsync(default) > 0) return;
        // Environment variable wins; the config keys exist for double-click use (remove the password from the file after first login).
        var password = Environment.GetEnvironmentVariable("AEGISWAF_BOOTSTRAP_ADMIN_PASSWORD") ?? app.Configuration["Bootstrap:AdminPassword"];
        var username = (Environment.GetEnvironmentVariable("AEGISWAF_BOOTSTRAP_ADMIN_USER") ?? app.Configuration["Bootstrap:AdminUser"] ?? "admin").Trim().ToLowerInvariant();
        var problem = PasswordHasher.Validate(password, username);
        if (problem is not null)
        {
            log.LogWarning("No users exist. Set AEGISWAF_BOOTSTRAP_ADMIN_PASSWORD (any non-empty password) and restart to create the first SUPER_ADMIN. {Problem}", problem);
            return;
        }
        var id = await store.CreateUserAsync(username, "Bootstrap administrator", PasswordHasher.Hash(password!), Roles.SuperAdmin, false, default);
        if (id is not null) await store.AuditAsync(null, "local", "user.bootstrap", username, "success", default);
        log.LogInformation("Created bootstrap SUPER_ADMIN '{User}'", username);
    }
}

public partial class Program { }

/// <summary>
/// Canonicalises a field before signature matching so that the common evasions (percent-encoding, double/triple encoding,
/// '+'-as-space, HTML entities, SQL /* */ comments, case) do not let an attack slip past the rules. It never changes the
/// request that is forwarded upstream — it only produces throw-away strings for inspection.
/// </summary>
static class Normalizer
{
    private const int Cap = 65536; // bound the work per field

    /// <summary>A small, de-duplicated set of canonical forms of <paramref name="raw"/> to run rules against.</summary>
    public static IReadOnlyList<string> Variants(string raw)
    {
        if (raw.Length > Cap) raw = raw[..Cap];
        var list = new List<string>(4);
        var lowerRaw = raw.ToLowerInvariant();
        list.Add(lowerRaw);

        var decoded = raw;
        for (var i = 0; i < 3; i++) // defeat double/triple encoding; also turns '+' into space (form encoding)
        {
            var step = SafeUrlDecode(decoded);
            if (step == decoded) break;
            decoded = step;
        }
        decoded = System.Net.WebUtility.HtmlDecode(decoded).ToLowerInvariant();
        AddIfNew(list, decoded);

        // Deep Unicode fold (NFKC): collapses fullwidth/compatibility forms an attacker uses to dodge ASCII rules,
        // e.g. fullwidth ＜ｓｃｒｉｐｔ＞ or the fraction-slash ⁄ become plain <script> and /. Also fold common homoglyph
        // separators (zero-width and BOM) away so "uni​on" cannot hide a keyword.
        var unicode = StripInvisible(SafeNfkc(decoded)).ToLowerInvariant();
        AddIfNew(list, unicode);

        var stripped = CollapseWhitespace(StripSqlComments(unicode));
        AddIfNew(list, stripped);
        return list;
    }

    private static void AddIfNew(List<string> list, string value)
    {
        foreach (var existing in list) if (existing == value) return;
        list.Add(value);
    }

    private static string SafeUrlDecode(string s)
    {
        try { return System.Net.WebUtility.UrlDecode(s) ?? s; }
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return s; }
    }

    private static string SafeNfkc(string s)
    {
        try { return s.IsNormalized(NormalizationForm.FormKC) ? s : s.Normalize(NormalizationForm.FormKC); }
        catch (ArgumentException) { return s; } // invalid/unpaired surrogates: leave as-is
    }

    /// <summary>Removes zero-width and BOM characters often inserted to split keywords (e.g. "sel​ect").</summary>
    private static string StripInvisible(string s)
    {
        if (s.All(c => c is not ('​' or '‌' or '‍' or '⁠' or '﻿' or '­'))) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (c is not ('​' or '‌' or '‍' or '⁠' or '﻿' or '­')) sb.Append(c);
        return sb.ToString();
    }

    private static string StripSqlComments(string s)
    {
        if (!s.Contains("/*", StringComparison.Ordinal)) return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                sb.Append(' '); i = end + 1;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var prevWs = false;
        foreach (var c in s)
        {
            var ws = char.IsWhiteSpace(c);
            if (ws) { if (!prevWs) sb.Append(' '); }
            else sb.Append(c);
            prevWs = ws;
        }
        return sb.ToString();
    }
}

sealed class SecurityRules
{
    private sealed record Rule(string Id, string Name, string Category, Regex Pattern) { public volatile bool Enabled = true; }

    // All patterns are NonBacktracking (linear time, no ReDoS) and run against the Normalizer's canonical forms.
    private static Regex R(string pattern) => new(pattern, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private readonly List<Rule> _rules =
    [
        new("sqli-01", "SQL Injection — UNION SELECT", "SQL Injection", R(@"union\s+(all\s+)?select")),
        new("sqli-02", "SQL Injection — boolean (OR/AND x=x)", "SQL Injection", R(@"['""\s](or|and)\s+['""]?[\w.]+['""]?\s*(=|like|<|>)\s*['""]?[\w.]+")),
        new("sqli-03", "SQL Injection — time/stacked/metadata", "SQL Injection", R(@"\b(sleep|pg_sleep|benchmark|dbms_pipe\.receive_message)\s*\(|waitfor\s+delay|;\s*(drop|alter|truncate|create|shutdown)\s|\b(information_schema|xp_cmdshell|into\s+(out|dump)file|load_file\s*\(|utl_http|extractvalue\s*\(|updatexml\s*\()")),
        new("xss-01", "XSS — script / scripting URI", "XSS", R(@"<\s*script|javascript\s*:|vbscript\s*:")),
        new("xss-02", "XSS — event handler attribute", "XSS", R(@"\bon(error|load|click|mouseover|mouseenter|mousemove|focus|blur|submit|change|input|keydown|keyup|keypress|animationstart|animationend|transitionend|toggle|pointerover|pointerenter|wheel|scroll|drag|drop|contextmenu|dblclick|beforeprint|hashchange)\s*=")),
        new("xss-03", "XSS — dangerous tag / sink", "XSS", R(@"<\s*(svg|img|iframe|object|embed|body|details|marquee|video|audio|math|form|input|link|meta|style|base|template|portal)\b|document\s*\.\s*(cookie|location|write)|window\s*\.\s*location|data\s*:\s*text/html|expression\s*\(")),
        new("path-01", "Path Traversal — dot-dot segment", "Path Traversal", R(@"\.\.[\\/]|\.\.%c0%af|\.\.%c1%9c")),
        new("path-02", "Path Traversal — sensitive file", "Path Traversal", R(@"/etc/(passwd|shadow|hosts|group|gshadow)\b|/proc/self/(environ|cmdline)|[\\/]windows[\\/](win\.ini|system32)|boot\.ini|\.ssh[\\/]id_rsa|web\.config\b|\.htpasswd")),
        new("lfi-01", "LFI/RFI — stream wrapper / scheme", "LFI/RFI", R(@"\b(php|phar|zip|expect|data|glob|file|dict|gopher|netdoc)\s*:\s*//|php://(filter|input|stdin)")),
        new("cmd-01", "Command Injection — shell operator + binary", "Command Injection", R(@"[;|&`\n][\s'""]*(cat|ls|id|whoami|pwd|uname|hostname|wget|curl|nc|ncat|telnet|bash|sh|zsh|cmd|powershell|pwsh|ping|nslookup|dig|chmod|chown|rm|mv|cp|echo|type|dir|net|ipconfig|ifconfig|tasklist|systeminfo|sleep|python|perl|ruby|php)\b")),
        new("cmd-02", "Command Injection — substitution / interpreter", "Command Injection", R(@"\$\(|\$\{[^}]|`[^`]*`|/bin/(ba|z|da)?sh\b|/bin/bash|cmd\.exe|powershell\.exe|/dev/tcp/")),
        new("ssrf-01", "SSRF — cloud metadata / internal", "SSRF", R(@"169\.254\.169\.254|metadata\.google\.internal|100\.100\.100\.200|fd00:ec2|/latest/meta-data|/computemetadata/")),
        new("proto-01", "Protocol Anomaly — null byte / CRLF injection", "Protocol Anomaly", R(@"\x00|%00|\r\n(set-cookie|location|content-length)\s*:")),
        new("nosql-01", "NoSQL Injection — operator / where", "NoSQL Injection", R(@"\$(ne|eq|gt|gte|lt|lte|nin|in|regex|where|exists|expr|elemmatch|function|accumulator|jsonschema)\b|\[\$(ne|gt|gte|lt|lte|in|nin|regex|where|exists)\]|\{\s*""?\$|\bthis\s*\.\s*password\b|;\s*return\s+(true|this)")),
        new("xxe-01", "XXE — external entity / DOCTYPE", "XXE", R(@"<!doctype[^>]*\[|<!\s*entity\b|<!\s*entity\s+\S+\s+(system|public)|(system|public)\s+[""'][^""']*(file|https?|ftp|php|expect|jar|netdoc)\s*:")),
        new("ssti-01", "Template Injection (SSTI)", "Template Injection", R(@"\{\{\s*\d+\s*[-+*/]\s*\d+\s*\}\}|\$\{\s*\d+\s*[-+*/]\s*\d+\s*\}|\{\{.{0,120}?(config|self|request|session|cycler|joiner|namespace|lipsum|__class__|__globals__|__subclasses__|__mro__|__import__|__builtins__|os\.|subprocess|popen|getruntime|freemarker|processbuilder).{0,120}?\}\}|\$\{.{0,120}?(class|getclass|runtime|getruntime|processbuilder|lang\.runtime|javax|exec\s*\().{0,120}?\}|<%.{0,160}?(runtime|processbuilder|exec\s*\(|request\.|system\s*\().{0,160}?%>|#\{.{0,120}?(class|runtime|exec|facescontext|t\s*\().{0,120}?\}|@\{.{0,120}?(system|process|exec).{0,120}?\}"))
    ];

    // Matched only against the User-Agent, where these names are high-signal and low false-positive.
    private readonly Rule _scanner = new("scanner-01", "Malicious Scanner / Tool", "Malicious Scanner",
        R(@"\b(sqlmap|nikto|nessus|netsparker|acunetix|nuclei|wpscan|havij|fimap|dirbuster|gobuster|hydra|masscan|zgrab|openvas|arachni|w3af|commix|xsstrike|feroxbuster|ffuf)\b"));

    // Score-based heuristic layer (anomaly scoring): several weak signals that individually should not block but,
    // when enough co-occur in one value, indicate obfuscated probing. Off by default because it raises false positives.
    private readonly AnomalyScorer _anomaly = new();

    public SecurityRules(IConfiguration cfg)
    {
        _anomaly.Threshold = Math.Clamp(cfg.GetValue("Waf:AnomalyScoring:Threshold", 7), 3, 100);
        _anomaly.Enabled = cfg.GetValue("Waf:AnomalyScoring:Enabled", false);
    }

    private readonly ConcurrentDictionary<string,(CustomRuleInput Input,Regex Pattern)> _custom = new();
    public void UpsertCustom(string id,CustomRuleInput input) => _custom[id]=(input,new Regex(input.Pattern,RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,TimeSpan.FromMilliseconds(20)));
    public void RemoveCustom(string id)=>_custom.TryRemove(id,out _);
    public object CustomList()=>_custom.OrderBy(x=>x.Value.Input.Priority).Select(x=>new {id=x.Key,name=x.Value.Input.Name,pattern=x.Value.Input.Pattern,priority=x.Value.Input.Priority,enabled=x.Value.Input.Enabled});
    public int Count => _rules.Count(x => x.Enabled) + (_scanner.Enabled ? 1 : 0) + (_anomaly.Enabled ? 1 : 0) + _custom.Count(x=>x.Value.Input.Enabled);
    public bool Exists(string id) => id == _scanner.Id || id == "anomaly-01" || _rules.Any(x => x.Id == id);
    public object List() => _rules.Append(_scanner)
        .Select(x => WithMetadata(x.Id, x.Name, x.Category, x.Enabled))
        .Append(WithMetadata("anomaly-01", "Anomaly Scoring — combined weak signals", "Anomaly Score", _anomaly.Enabled));
    private static object WithMetadata(string id, string name, string category, bool enabled)
    {
        var metadata = OwaspCatalog.ForRule(id);
        return new { id, name, category, enabled, owaspCodes = metadata.Codes, coverage = metadata.Coverage.ToString(), description = metadata.Description, limitations = metadata.Limitations };
    }
    public bool Toggle(string id, bool enabled)
    {
        if (id == "anomaly-01") { _anomaly.Enabled = enabled; return true; }
        var rule = id == _scanner.Id ? _scanner : _rules.FirstOrDefault(x => x.Id == id);
        if (rule is null) return false;
        rule.Enabled = enabled;
        return true;
    }

    /// <summary>Runs custom rules then built-in rules against every canonical form of <paramref name="input"/>.</summary>
    public string? Match(string input, bool checkOversize = true)
    {
        if (checkOversize && input.Length > 8192) return "Oversized URL";
        var variants = Normalizer.Variants(input);
        foreach (var entry in _custom.OrderBy(x => x.Value.Input.Priority))
        {
            if (!entry.Value.Input.Enabled) continue;
            try { foreach (var v in variants) if (entry.Value.Pattern.IsMatch(v)) return "Custom: " + entry.Key; }
            catch (RegexMatchTimeoutException) { return "Inspection Timeout"; }
        }
        foreach (var rule in _rules)
        {
            if (!rule.Enabled) continue;
            try { foreach (var v in variants) if (rule.Pattern.IsMatch(v)) return rule.Category; }
            catch (RegexMatchTimeoutException) { return "Inspection Timeout"; }
        }
        if (_anomaly.Enabled)
        {
            try { if (_anomaly.Score(variants) >= _anomaly.Threshold) return "Anomaly Score"; }
            catch (RegexMatchTimeoutException) { return "Inspection Timeout"; }
        }
        return null;
    }

    /// <summary>Scanner / attack-tool fingerprint in the User-Agent (checked separately to avoid body false-positives).</summary>
    public string? MatchUserAgent(string userAgent)
    {
        if (!_scanner.Enabled || string.IsNullOrEmpty(userAgent)) return null;
        try { return _scanner.Pattern.IsMatch(userAgent) ? _scanner.Category : null; }
        catch (RegexMatchTimeoutException) { return "Inspection Timeout"; }
    }
}

/// <summary>
/// Heuristic score for one normalised value. No single signal blocks; the request is flagged only when enough weak
/// signals co-occur (total ≥ threshold). This catches obfuscated probing that each individual signature would miss,
/// at the cost of more false positives — hence disabled by default and individually toggleable (rule "anomaly-01").
/// </summary>
sealed class AnomalyScorer
{
    public volatile bool Enabled;
    public int Threshold = 7;

    // NonBacktracking keeps these expressions linear; 100 ms avoids false timeout blocks on a busy host
    // while still bounding a single anomaly evaluation under load.
    private static Regex R(string p) => new(p, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly (Regex Pattern, int Weight)[] _signals =
    [
        (R(@"\b(select|union|insert|update|delete|drop|where|from|having|concat|substring|char|cast|convert)\b"), 3),
        (R(@"(--|#|/\*|\*/|;)"), 2),
        (R(@"['""]\s*(or|and)\s|['""]\s*=\s*['""]|\b1\s*=\s*1\b"), 3),
        (R(@"<|>|%3c|%3e"), 2),
        (R(@"\b(script|alert|onerror|onload|eval|prompt|document|window|cookie|iframe|svg)\b"), 2),
        (R(@"\$\(|\$\{|`|\|\||&&|;\s*\w|\|\s*\w"), 3),
        (R(@"\.\.[\\/]|/etc/|/proc/|\\windows\\|boot\.ini|web\.config"), 3),
        (R(@"\b(cat|whoami|id|uname|wget|curl|nc|bash|sh|cmd|powershell|ping|nslookup)\b"), 2),
        (R(@"169\.254|metadata|127\.0\.0\.1|localhost|file:|php:|gopher:|dict:"), 2),
        (R(@"\$(ne|gt|gte|lt|lte|where|regex|exists)\b|\{\{|\}\}|<%|%>|__class__|__globals__"), 3),
    ];

    /// <summary>The highest score across the supplied canonical variants.</summary>
    public int Score(IReadOnlyList<string> variants)
    {
        var best = 0;
        foreach (var v in variants)
        {
            var s = 0;
            foreach (var (pattern, weight) in _signals)
                if (pattern.IsMatch(v)) s += weight;
            if (s > best) best = s;
        }
        return best;
    }
}

/// <summary>
/// Scans the response the upstream sent back for signs that an attack succeeded and secrets are leaking out:
/// the contents of a shadow/passwd file, framework stack traces, or raw database error messages. If a textual
/// response under the size cap matches, the body is suppressed and replaced by a generic 502 so the leak never
/// reaches the client. Off switch: <c>Waf:InspectResponses=false</c>. Size cap: <c>Waf:MaxResponseInspectBytes</c>.
/// </summary>
sealed class ResponseInspector
{
    private static Regex R(string p) => new(p, RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    private readonly (string Name, Regex Pattern)[] _leaks =
    [
        ("Response Leak — passwd/shadow file", R(@"root:.*:0:0:|^[\w-]+:\$\d\$")),
        ("Response Leak — private key", R(@"-----begin (rsa |ec |openssh |dsa )?private key-----")),
        ("Response Leak — SQL error", R(@"(sql syntax.*mysql|you have an error in your sql syntax|unclosed quotation mark after the character string|npgsql\.postgresexception|ora-\d{5}|psqlexception|sqlstate\[|warning: mysqli?_)")),
        ("Response Leak — stack trace", R(@"(system\.[\w.]+exception:|\bat [\w.<>]+\([\w.: ]*\)\s+in |traceback \(most recent call last\)|java\.lang\.[\w.]+exception|org\.springframework|\.php on line \d+|stack trace:)")),
    ];

    /// <summary>Returns the matched leak name, or null when the body looks clean.</summary>
    public string? Inspect(string body)
    {
        foreach (var (name, pattern) in _leaks)
            try { if (pattern.IsMatch(body)) return name; }
            catch (RegexMatchTimeoutException) { return "Response Inspection Timeout"; }
        return null;
    }

    /// <summary>True for content types whose body is worth scanning (html, text, json, xml). Binary responses are skipped.</summary>
    public static bool IsInspectable(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false; // unknown/binary: don't buffer
        var c = contentType.AsSpan();
        return c.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("html", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Captures the proxied response body so it can be scanned before the client sees it. While the body stays under
/// <c>cap</c> bytes everything is held in memory and nothing reaches the client, so the middleware can still block.
/// The moment the body exceeds <c>cap</c> it flushes what it has and streams the rest straight through
/// (<see cref="Overflowed"/> = true): large responses are forwarded unbuffered and are not inspected (fail-open).
/// </summary>
sealed class ResponseBufferStream(Stream target, int cap, bool failClosedOverflow = false) : Stream
{
    private readonly MemoryStream _buffer = new();
    public bool Overflowed { get; private set; }
    public byte[] CapturedBuffer => _buffer.GetBuffer();
    public int CapturedLength => (int)_buffer.Length;

    public override bool CanWrite => true;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override long Length => _buffer.Length;
    public override long Position { get => _buffer.Position; set => throw new NotSupportedException(); }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> src, CancellationToken ct = default)
    {
        if (Overflowed) { await target.WriteAsync(src, ct); return; }
        if (_buffer.Length + src.Length <= cap) { await _buffer.WriteAsync(src, ct); return; }
        // Crossing the cap: either fail closed (nothing has reached the client) or preserve the
        // compatibility streaming behavior for explicitly configured non-production deployments.
        Overflowed = true;
        if (failClosedOverflow) return;
        _buffer.Position = 0;
        await _buffer.CopyToAsync(target, ct);
        _buffer.SetLength(0);
        await target.WriteAsync(src, ct);
    }

    public override Task WriteAsync(byte[] b, int off, int count, CancellationToken ct) => WriteAsync(b.AsMemory(off, count), ct).AsTask();
    public override void Write(byte[] b, int off, int count) => WriteAsync(b.AsMemory(off, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Overflowed && !failClosedOverflow ? target.FlushAsync(ct) : Task.CompletedTask;
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
}
sealed class TrafficCounters
{
    private long _total, _blocked;
    private readonly ConcurrentDictionary<string, long> _categories = new();
    private readonly ConcurrentDictionary<string, long> _methods = new();
    private readonly ConcurrentDictionary<string, long> _ips = new();
    private readonly ConcurrentDictionary<long, (long Requests, long Blocked)> _minutes = new();
    public long Total => Interlocked.Read(ref _total);
    public long Blocked => Interlocked.Read(ref _blocked);
    public long Allowed => Total - Blocked;
    public IReadOnlyDictionary<string,long> Categories => _categories;
    public IReadOnlyDictionary<string,long> Methods => _methods;
    public void Record(string method, string ip, string? threat)
    {
        Interlocked.Increment(ref _total);
        _methods.AddOrUpdate(method, 1, (_, old) => old + 1);
        if (threat != null)
        {
            Interlocked.Increment(ref _blocked);
            _categories.AddOrUpdate(threat, 1, (_, old) => old + 1);
            _ips.AddOrUpdate(ip, 1, (_, old) => old + 1);
        }
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        _minutes.AddOrUpdate(minute, (1, threat is null ? 0 : 1), (_, old) => (old.Requests + 1, old.Blocked + (threat is null ? 0 : 1)));
        if (_minutes.Count > 1500) foreach (var key in _minutes.Keys.Where(x => x < minute - 1440)) _minutes.TryRemove(key, out _);
    }
    public object Timeline() => Enumerable.Range(0, 24).Select(i =>
    {
        var hour = DateTimeOffset.UtcNow.AddHours(i - 23);
        var start = hour.ToUnixTimeSeconds() / 3600 * 60;
        long req = 0, blocked = 0;
        for (var minute = start; minute < start + 60; minute++)
            if (_minutes.TryGetValue(minute, out var v)) { req += v.Requests; blocked += v.Blocked; }
        return new { time = hour.ToString("HH:00"), requests = req, blocked };
    });
    public object TopIps() => _ips.OrderByDescending(x => x.Value).Take(10).Select(x => new { ip = x.Key, blocked = x.Value });
}
record SecurityEvent(DateTimeOffset Timestamp, string Ip, string Method, string Path, string Rule, int Status, string? RequestId = null,
    string? SiteId = null, string? Hostname = null, string? UpstreamId = null);
sealed class SecurityEvents(ILogger<SecurityEvents> logger)
{
    private readonly Channel<SecurityEvent> _queue = Channel.CreateBounded<SecurityEvent>(new BoundedChannelOptions(10000)
    { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly ConcurrentQueue<SecurityEvent> _recent = new();
    private long _dropped;
    public long Dropped => Interlocked.Read(ref _dropped);
    public bool TryWrite(SecurityEvent item)
    {
        _recent.Enqueue(item);
        while (_recent.Count > 100 && _recent.TryDequeue(out _)) { }
        if (_queue.Writer.TryWrite(item)) return true;
        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % 100 == 0)
            logger.LogError("WAF_SECURITY_EVENT_DROPPED {DroppedCount} {SecurityEvent}", dropped, SecurityEventLogFormatter.Format(item));
        return false;
    }
    public object Recent() => _recent.Reverse().Take(20).Select(x => new { time = x.Timestamp, ip = x.Ip, method = x.Method, path = x.Path, rule = x.Rule, status = x.Status, owaspCodes = OwaspCatalog.ForRule(x.Rule).Codes });
    public IAsyncEnumerable<SecurityEvent> ReadAll(CancellationToken token) => _queue.Reader.ReadAllAsync(token);
}
sealed class EventWriter(SecurityEvents events, IConfiguration config, ILogger<EventWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var connection = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connection)) { logger.LogWarning("No PostgreSQL connection configured"); return; }
        await foreach (var item in events.ReadAll(token))
        {
            if (config.GetValue("Waf:Events:StructuredLogging", true))
                logger.LogWarning("WAF_SECURITY_EVENT {SecurityEvent}", SecurityEventLogFormatter.Format(item));
            try
            {
                await using var db = new NpgsqlConnection(connection);
                await db.OpenAsync(token);
                await using var cmd = new NpgsqlCommand("INSERT INTO security_events (occurred_at,client_ip,http_method,request_path,rule_name,status_code,site_id,hostname,upstream_id) VALUES (@t,@ip,@m,@p,@r,@s,@site,@host,@upstream)", db);
                cmd.Parameters.AddWithValue("t", item.Timestamp);
                cmd.Parameters.AddWithValue("ip", item.Ip);
                cmd.Parameters.AddWithValue("m", item.Method);
                cmd.Parameters.AddWithValue("p", item.Path);
                cmd.Parameters.AddWithValue("r", item.Rule);
                cmd.Parameters.AddWithValue("s", item.Status);
                cmd.Parameters.AddWithValue("site", (object?)item.SiteId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("host", (object?)item.Hostname ?? DBNull.Value);
                cmd.Parameters.AddWithValue("upstream", (object?)item.UpstreamId ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Security event persistence failed"); }
        }
    }
}

sealed class RuleStateLoader(SecurityRules rules, IConfiguration config, ILogger<RuleStateLoader> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken token)
    {
        var cs = config.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(cs)) return;
        try
        {
            await using var db = new NpgsqlConnection(cs);
            await db.OpenAsync(token);
            await using var cmd = new NpgsqlCommand("SELECT rule_id,enabled FROM waf_rule_states", db);
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) rules.Toggle(reader.GetString(0), reader.GetBoolean(1));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Could not restore rule states. Run database/003_rule_states.sql"); }
    }
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
}

sealed class CustomRuleLoader(SecurityRules rules,IConfiguration cfg,ILogger<CustomRuleLoader> logger):IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try {await using var db=new NpgsqlConnection(cfg.GetConnectionString("Postgres"));await db.OpenAsync(ct);
            await using var cmd=new NpgsqlCommand("SELECT id,name,pattern,priority,enabled FROM waf_custom_rules",db);
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) {
                try {rules.UpsertCustom(reader.GetString(0),new CustomRuleInput(reader.GetString(1),reader.GetString(2),reader.GetInt32(3),reader.GetBoolean(4)));}
                catch(ArgumentException ex){logger.LogError(ex,"Invalid persisted rule {RuleId}",reader.GetString(0));}
            }
        }catch(Exception ex){logger.LogWarning(ex,"Custom rule restoration failed; run 005_custom_rules.sql");}
    }
    public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
}
