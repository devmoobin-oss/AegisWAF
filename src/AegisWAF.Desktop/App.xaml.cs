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
    /// First run: setup wizard (starts the gateway). Afterwards: main window directly — there is no login —
    /// repeating after a settings change or reset, until the user closes the window.
    /// </summary>
    private static void RunSessions()
    {
        while (true)
        {
            var settings = SettingsStore.Load();
            if (settings is null)
            {
                var wizard = new SetupWindow();
                if (wizard.ShowDialog() != true) return;
                settings = wizard.Settings!;
            }
            else if (!Gateway.Running)
            {
                try { Gateway.StartAsync(settings).GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    Gateway.StopAsync().GetAwaiter().GetResult();
                    MessageBox.Show("เริ่มระบบป้องกันไม่สำเร็จ: " + GatewayStartupFailure.Describe(ex.Message, settings.ListenPort), "AegisWAF", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            Info = new ConnectionInfo(true, settings.ListenUrl, settings.UpstreamUrl);
            using var api = new ApiClient(settings.ListenUrl);
            var main = new MainWindow(api);
            main.ShowDialog();
            if (main.ResetRequested) { Gateway.Dispose(); SettingsStore.Delete(); continue; }
            if (!main.RestartRequested) return;
        }
    }
}
