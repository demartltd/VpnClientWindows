using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DemarVpn.Domain.Vpn.Tun;
using DemarVpn.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DemarVpn.Views;

public sealed partial class TunSettingsPage
{
    private async Task PickExeAsync()
    {
        if (_picking || XamlRoot is null) return;

        string? picked = null;
        var list = new ListView
        {
            MaxHeight = 220,
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
        };
        foreach (var (name, icon) in RunningExes())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Tag = name };
            row.Children.Add(icon is not null
                ? new Image { Width = 20, Height = 20, Source = icon }
                : new FontIcon { Glyph = "\uE8A5", FontSize = 16, Opacity = 0.55, Width = 20 });
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(row);
        }

        var dialog = DemarDialog.Create(XamlRoot, Strings.SettingsTunAppsAdd, new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Strings.SettingsTunAppsRunning, Opacity = 0.7, FontSize = 12 },
                list,
            },
        });
        dialog.PrimaryButtonText = Strings.SettingsTunAppsPickExplorer;
        dialog.SecondaryButtonText = Strings.SettingsTunAppsTypeName;
        dialog.CloseButtonText = Strings.Cancel;
        list.ItemClick += (_, e) =>
        {
            picked = (e.ClickedItem as FrameworkElement)?.Tag as string;
            dialog.Hide();
        };

        var result = await dialog.ShowAsync();
        if (picked is not null) AppServices.Vpn.AddBypassExe(picked);
        else if (result == ContentDialogResult.Primary) await PickExeFromDiskAsync();
        else if (result == ContentDialogResult.Secondary) await PromptExeNameAsync();
    }

    private static List<(string Name, ImageSource? Icon)> RunningExes()
    {
        var map = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                var id = TunExeNames.Normalize(p.ProcessName + ".exe");
                if (!TunExeNames.IsExeName(id) || TunExeNames.IsSelf(id) || map.ContainsKey(id)) continue;
                map[id] = TryExeIcon(TryExePath(p));
            }
            finally { p.Dispose(); }
        }
        return map.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static string? TryExePath(Process p)
    {
        try
        {
            var sb = new StringBuilder(1024);
            var n = sb.Capacity;
            return QueryFullProcessImageName(p.Handle, 0, sb, ref n) ? sb.ToString() : null;
        }
        catch { return null; }
    }

    private static ImageSource? TryExeIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var image = new BitmapImage();
            image.SetSource(ms.AsRandomAccessStream());
            return image;
        }
        catch { return null; }
    }

    private async Task PromptExeNameAsync()
    {
        if (XamlRoot is null) return;
        var input = new TextBox { PlaceholderText = Strings.SettingsTunAppsTypeHint, MinWidth = 280 };
        if (await DemarDialog.ChooseAsync(
                XamlRoot, Strings.SettingsTunAppsTypeName, input,
                Strings.SettingsTunAppsAdd, closeButtonText: Strings.Cancel) != ContentDialogResult.Primary)
            return;
        var raw = input.Text.Trim();
        if (raw.Length == 0) return;
        if (!TunExeNames.IsExeName(raw))
        {
            AppServices.Snackbar.ShowError(Strings.SettingsTunAppsMustExe);
            return;
        }
        AppServices.Vpn.AddBypassExe(raw);
    }

    private async Task PickExeFromDiskAsync()
    {
        if (_picking || XamlRoot is null) return;
        _picking = true;
        try
        {
            if (App.MainHwnd == 0)
                throw new InvalidOperationException(Strings.SettingsTunAppsPickUnavailable);
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, App.MainHwnd);
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".exe");
            var file = await picker.PickSingleFileAsync();
            if (file is not null) AppServices.Vpn.AddBypassExe(file.Path);
        }
        catch (Exception ex)
        {
            AppLog.Append($"tun_apps: pick failed — {ex.Message}");
            AppServices.Snackbar.ShowError(ex.Message);
        }
        finally { _picking = false; }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);
}
