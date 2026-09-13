using System.Globalization;

namespace DemarVpn.Installer;

/// <summary>Installer/Uninstall UI strings. Language follows OS UI culture (ru → ru, else en).</summary>
static class InstallerStrings
{
    static bool IsRu =>
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase);

    static string T(string en, string ru) => IsRu ? ru : en;

    public static string AlreadyRunning => T("Demar VPN installer is already running.", "Установщик Demar VPN уже запущен.");
    public static string ChromeInstall => T("Demar VPN — Setup", "Demar VPN — Установка");
    public static string ChromeUninstall => T("Demar VPN — Uninstall", "Demar VPN — Удаление");
    public static string Close => T("Close", "Закрыть");
    public static string SubtitleInstall => T("Installs the latest version from this package.", "Установит последнюю версию из этого пакета.");
    public static string SubtitleUninstall => T("Removes the app, shortcut, and installer entries.", "Удалит приложение, ярлык и записи установщика.");
    public static string InstallFolder => T("Install folder", "Папка установки");
    public static string Browse => T("Browse…", "Обзор…");
    public static string BrowseTitle => T("Demar VPN install folder", "Папка установки Demar VPN");
    public static string Install => T("Install", "Установить");
    public static string Uninstall => T("Uninstall", "Удалить");
    public static string Open => T("Open", "Открыть");
    public static string Installed => T("Installed", "Установлено");
    public static string Uninstalled => T("Uninstalled", "Удалено");
    public static string Error => T("Error", "Ошибка");
    public static string Stopping => T("Stopping processes…", "Остановка процессов…");
    public static string Cleaning => T("Cleaning old files…", "Очистка старых файлов…");
    public static string Extracting => T("Extracting…", "Распаковка…");
    public static string Registering => T("Registering…", "Регистрация…");
    public static string Done => T("Done", "Готово");
    public static string Removing => T("Removing…", "Удаление…");
    public static string Finishing => T("Finishing…", "Завершение…");
    public static string EmptyInstallPath => T("Install path is empty", "Путь установки пуст");
}
