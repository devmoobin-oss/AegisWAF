using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AegisWAF.Gateway;

namespace AegisWAF.Desktop;

/// <summary>Everything the in-app gateway needs to start. The administrator's password is never part of this.</summary>
public sealed record GatewaySettings(int Version, string Host, int Port, string Database, string Username, string Password, int ListenPort, string UpstreamUrl)
{
    public const int CurrentVersion = 2;
    public const int DefaultListenPort = 5080;
    public const string DefaultUpstream = "http://localhost:8080/";

    public DbParams Db => new(Host, Port, Database, Username, Password);
    public Uri ListenUrl => new($"http://127.0.0.1:{ListenPort}/");

    public static GatewaySettings Create(DbParams db, int listenPort, string upstream) =>
        new(CurrentVersion, db.Host, db.Port, db.Database, db.Username, db.Password, listenPort, upstream);
}

/// <summary>What the app is currently connected to (shown on the dashboard).</summary>
public sealed record ConnectionInfo(bool Embedded, Uri Url, string? Upstream);

/// <summary>
/// Persists <see cref="GatewaySettings"/> under %AppData%\AegisWAF, encrypted with Windows DPAPI (current-user scope):
/// unreadable by other Windows accounts and useless if copied to another machine.
/// </summary>
public static class SettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AegisWAF.GatewaySettings.v2");
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AegisWAF", "gateway.settings");

    public static GatewaySettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var s = JsonSerializer.Deserialize<GatewaySettings>(plain);
            return s is { Version: GatewaySettings.CurrentVersion } && !string.IsNullOrWhiteSpace(s.Host) ? s : null;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // unreadable / corrupt / older format: behave as "not configured yet" and show the setup wizard
        }
    }

    public static void Save(GatewaySettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(s), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Delete()
    {
        try { File.Delete(FilePath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
