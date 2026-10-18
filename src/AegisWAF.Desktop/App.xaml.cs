using System.Windows;
using AegisWAF.Client;

namespace AegisWAF.Desktop;

public partial class App : Application
{
    /// <summary>The in-process gateway; stopped when the app exits.</summary>
    public static LocalGateway Gateway { get; } = new();
    /// <summary>What the signed-in session is connected to (for the dashboard banner and settings page).</summary>
    public static ConnectionInfo? Info { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show("เกิดข้อผิดพลาดที่ไม่คาดคิด: " + ex.Exception.Message, "AegisWAF", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        RunSessions();
        Gateway.Dispose();
        Shutdown();
    }

    /// <summary>
    /// First run: setup wizard (starts the gateway and signs in). Afterwards: login → main window, repeating after logout,
    /// session expiry or a settings change, until the user closes a window.
    /// </summary>
    private static void RunSessions()
    {
        ApiClient? ready = null;       // set by the wizard: already signed in
        string? lastUser = null;
        while (true)
        {
            if (SettingsStore.Load() is null)
            {
                var wizard = new SetupWindow();
                if (wizard.ShowDialog() != true) return;
                if (wizard.Api is not null) { ready = wizard.Api; Info = new ConnectionInfo(true, wizard.Settings!.ListenUrl, wizard.Settings.UpstreamUrl); }
            }

            ApiClient api;
            if (ready is not null) { api = ready; ready = null; }
            else
            {
                var login = new LoginWindow(lastUser);
                if (login.ShowDialog() != true)
                {
                    if (login.ResetRequested) { Gateway.Dispose(); SettingsStore.Delete(); continue; }
                    return;
                }
                api = login.Api!; Info = login.Info;
            }
            lastUser = api.Session!.Username;

            if (api.Session.MustChangePassword)
            {
                if (new ChangePasswordWindow(api, forced: true).ShowDialog() != true)
                {
                    try { api.LogoutAsync().GetAwaiter().GetResult(); } catch (ApiException) { }
                    return;
                }
            }

            var main = new MainWindow(api);
            main.ShowDialog();
            if (main.ResetRequested) { Gateway.Dispose(); SettingsStore.Delete(); continue; }
            if (!main.ReturnToLogin) return;
        }
    }
}
