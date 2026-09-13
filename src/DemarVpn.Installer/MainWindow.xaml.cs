using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace DemarVpn.Installer;

public partial class MainWindow : Window
{
    readonly bool _uninstall;
    bool _busy;
    string? _installDir;

    public MainWindow(bool uninstall)
    {
        _uninstall = uninstall;
        InitializeComponent();

        var ver = InstallService.AppVersion;
        TitleText.Text = $"Demar VPN  {ver}";
        ChromeTitle.Text = _uninstall ? InstallerStrings.ChromeUninstall : InstallerStrings.ChromeInstall;
        SubtitleText.Text = _uninstall ? InstallerStrings.SubtitleUninstall : InstallerStrings.SubtitleInstall;
        ActionButton.Content = _uninstall ? InstallerStrings.Uninstall : InstallerStrings.Install;
        FolderLabel.Text = InstallerStrings.InstallFolder;
        BrowseBtn.Content = InstallerStrings.Browse;
        CloseBtn.ToolTip = InstallerStrings.Close;

        if (_uninstall)
        {
            PathPanel.Visibility = Visibility.Collapsed;
            PathBox.Text = InstallService.ResolveInstallDirFromArp();
        }
        else
        {
            PathBox.Text = InstallService.ResolveTargetInstallDir();
        }
    }

    void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = InstallerStrings.BrowseTitle,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(PathBox.Text) && Directory.Exists(PathBox.Text))
            dlg.InitialDirectory = PathBox.Text;
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
            PathBox.Text = IOPath.Combine(dlg.FolderName, InstallService.AppName);
    }

    async void Action_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ActionButton.IsEnabled = false;
        PathBox.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        var progress = new Progress<string>(s => StatusText.Text = s);
        try
        {
            var dir = PathBox.Text;
            await Task.Run(() =>
            {
                if (_uninstall) InstallService.Uninstall(progress);
                else InstallService.Install(dir, progress);
            });
            StatusText.Text = _uninstall ? InstallerStrings.Uninstalled : InstallerStrings.Installed;
            ActionButton.Click -= Action_OnClick;
            if (_uninstall)
            {
                ActionButton.Content = InstallerStrings.Close;
                ActionButton.Click += (_, _) => Close();
            }
            else
            {
                _installDir = dir;
                ActionButton.Content = InstallerStrings.Open;
                ActionButton.Click += Open_OnClick;
            }
            ActionButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ActionButton.IsEnabled = true;
            PathBox.IsEnabled = !_uninstall;
            ActionButton.Content = _uninstall ? InstallerStrings.Uninstall : InstallerStrings.Install;
            _busy = false;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    void Open_OnClick(object sender, RoutedEventArgs e)
    {
        InstallService.LaunchApp(_installDir ?? PathBox.Text);
        Close();
    }
}
