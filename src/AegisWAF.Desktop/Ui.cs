using System.Windows;
using System.Windows.Media;
using AegisWAF.Client;

namespace AegisWAF.Desktop;

internal static class Ui
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    public static string Describe(Exception ex) => ex switch
    {
        ApiException { Status: 403 } a => "สิทธิ์ไม่เพียงพอ: " + a.Message,
        ApiException { Status: 401 } => "เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่",
        ApiException a => a.Message,
        _ => ex.Message
    };

    public static bool Confirm(Window? owner, string message) =>
        MessageBox.Show(owner ?? Application.Current.MainWindow, message, "AegisWAF", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>The only rules: not empty, and at most 128 characters (limits hashing cost). Same as the server.</summary>
    public static string? PasswordProblem(string password)
    {
        if (password.Length == 0) return "กรุณากรอกรหัสผ่าน";
        if (password.Length > 128) return "รหัสผ่านยาวเกิน 128 ตัวอักษร";
        return null;
    }

    public static string Compact(long n) => n >= 1_000_000 ? (n / 1_000_000.0).ToString("0.#") + "M" : n >= 1_000 ? (n / 1_000.0).ToString("0.#") + "k" : n.ToString();
    public static string Pct(long n, long total) => total > 0 ? (100.0 * n / total).ToString("0.0") + "%" : "0%";
}

/// <summary>Implemented by every page hosted in the main window.</summary>
public interface IPage
{
    Task RefreshAsync();
    /// <summary>True for live pages that are refreshed on a timer.</summary>
    bool AutoRefresh { get; }
}
