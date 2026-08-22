using System.Windows;

namespace DemarVpn.Installer;

public partial class App : Application
{
    Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Global\DemarVpn.Updater", out var created);
        if (!created)
        {
            MessageBox.Show("Установщик Demar VPN уже запущен.", "Demar VPN",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1);
            return;
        }

        var mode = InstallerMode.Detect();
        if (mode.Silent)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                if (mode.Uninstall)
                {
                    InstallService.Uninstall(null);
                }
                else
                {
                    var installDir = mode.InstallDir ?? InstallService.ResolveTargetInstallDir();
                    InstallService.Install(installDir, null);
                    if (mode.LaunchAfter)
                    {
                        InstallService.LaunchApp(installDir);
                        InstallService.ScheduleSelfDeleteAfterUpdate();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Demar VPN", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            Shutdown(0);
            return;
        }

        MainWindow = new MainWindow(mode.Uninstall);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
