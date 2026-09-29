using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DemarVpn.Installer;

/// <summary>Installer/Uninstall UI strings. Language follows OS UI culture (same tags as app).</summary>
static class InstallerStrings
{
    // Order must match LocaleStore.Supported
    static readonly string[] Tags =
        ["en", "ru", "uk", "tr", "uz", "zh-Hans", "zh-Hant", "ko", "ja", "es", "de", "fr", "hi"];

    static readonly int Ix = ResolveIndex();

    static string T(params string[] xs) => xs[Ix < xs.Length ? Ix : 0];

    static int ResolveIndex()
    {
        var tag = Match(OsUiCulture()) ?? "en";
        var i = Array.FindIndex(Tags, t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : 0;
    }

    static string OsUiCulture()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture.Name;
            if (!string.IsNullOrEmpty(name) &&
                !name.Equals("iv", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals(CultureInfo.InvariantCulture.Name, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        catch { /* invariant globalization */ }

        var sb = new StringBuilder(85);
        return GetUserDefaultLocaleName(sb, sb.Capacity) > 0 ? sb.ToString() : "en";
    }

    static string? Match(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace('_', '-');

        foreach (var tag in Tags)
            if (string.Equals(tag, s, StringComparison.OrdinalIgnoreCase))
                return tag;

        if (IsZhHans(s)) return "zh-Hans";
        if (IsZhHant(s)) return "zh-Hant";
        if (s.Equals("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";

        try
        {
            for (var c = CultureInfo.GetCultureInfo(s); !string.IsNullOrEmpty(c.Name); c = c.Parent)
            {
                if (IsZhHans(c.Name)) return "zh-Hans";
                if (IsZhHant(c.Name)) return "zh-Hant";
                foreach (var tag in Tags)
                {
                    if (string.Equals(tag, c.Name, StringComparison.OrdinalIgnoreCase)) return tag;
                    if (tag.Length == 2 &&
                        string.Equals(tag, c.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
                        return tag;
                }
            }
        }
        catch (CultureNotFoundException) { /* AOT / invariant */ }

        if (s.Length >= 2)
        {
            var two = s[..2];
            foreach (var tag in Tags)
                if (tag.Length == 2 && string.Equals(tag, two, StringComparison.OrdinalIgnoreCase))
                    return tag;
        }
        return null;
    }

    static bool IsZhHans(string n) =>
        n.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-SG", StringComparison.OrdinalIgnoreCase);

    static bool IsZhHant(string n) =>
        n.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
        n.Equals("zh-MO", StringComparison.OrdinalIgnoreCase);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetUserDefaultLocaleName(StringBuilder lpLocaleName, int cchLocaleName);

    public static string AlreadyRunning => T("Demar VPN installer is already running.", "Установщик Demar VPN уже запущен.", "Встановлювач Demar VPN уже запущено.", "Demar VPN yükleyicisi zaten çalışıyor.", "Demar VPN o‘rnatuvchisi allaqachon ishlamoqda.", "Demar VPN 安装程序已在运行。", "Demar VPN 安裝程式已在執行。", "Demar VPN 설치 프로그램이 이미 실행 중입니다.", "Demar VPNインストーラーはすでに実行中です。", "El instalador de Demar VPN ya está en ejecución.", "Das Demar-VPN-Setup wird bereits ausgeführt.", "Le programme d’installation Demar VPN est déjà en cours d’exécution.", "Demar VPN इंस्टॉलर पहले से चल रहा है।");

    public static string ChromeInstall => T("Demar VPN — Setup", "Demar VPN — Установка", "Demar VPN — Встановлення", "Demar VPN — Kurulum", "Demar VPN — O‘rnatish", "Demar VPN — 安装", "Demar VPN — 安裝", "Demar VPN — 설치", "Demar VPN — セットアップ", "Demar VPN — Instalación", "Demar VPN — Setup", "Demar VPN — Installation", "Demar VPN — सेटअप");

    public static string ChromeUninstall => T("Demar VPN — Uninstall", "Demar VPN — Удаление", "Demar VPN — Видалення", "Demar VPN — Kaldırma", "Demar VPN — O‘chirish", "Demar VPN — 卸载", "Demar VPN — 解除安裝", "Demar VPN — 제거", "Demar VPN — アンインストール", "Demar VPN — Desinstalación", "Demar VPN — Deinstallation", "Demar VPN — Désinstallation", "Demar VPN — अनइंस्टॉल");

    public static string Close => T("Close", "Закрыть", "Закрити", "Kapat", "Yopish", "关闭", "關閉", "닫기", "閉じる", "Cerrar", "Schließen", "Fermer", "बंद करें");

    public static string SubtitleInstall => T("Installs the latest version from this package.", "Установит последнюю версию из этого пакета.", "Встановить останню версію з цього пакета.", "Bu paketten en son sürümü yükler.", "Ushbu paketdan eng so‘nggi versiyani o‘rnatadi.", "从此包安装最新版本。", "從此套件安裝最新版本。", "이 패키지에서 최신 버전을 설치합니다.", "このパッケージから最新バージョンをインストールします。", "Instala la última versión de este paquete.", "Installiert die neueste Version aus diesem Paket.", "Installe la dernière version depuis ce package.", "इस पैकेज से नवीनतम संस्करण इंस्टॉल करता है।");

    public static string SubtitleUninstall => T("Removes the app, shortcut, and installer entries.", "Удалит приложение, ярлык и записи установщика.", "Видалить застосунок, ярлик і записи встановлювача.", "Uygulamayı, kısayolu ve yükleyici kayıtlarını kaldırır.", "Ilova, yorliq va o‘rnatuvchi yozuvlarini o‘chiradi.", "将删除应用、快捷方式和安装程序条目。", "將移除應用程式、捷徑與安裝程式項目。", "앱, 바로 가기 및 설치 프로그램 항목을 제거합니다.", "アプリ、ショートカット、インストーラーの登録を削除します。", "Elimina la app, el acceso directo y las entradas del instalador.", "Entfernt die App, die Verknüpfung und Setup-Einträge.", "Supprime l’app, le raccourci et les entrées du programme d’installation.", "ऐप, शॉर्टकट और इंस्टॉलर प्रविष्टियाँ हटाता है।");

    public static string InstallFolder => T("Install folder", "Папка установки", "Папка встановлення", "Kurulum klasörü", "O‘rnatish papkasi", "安装文件夹", "安裝資料夾", "설치 폴더", "インストールフォルダー", "Carpeta de instalación", "Installationsordner", "Dossier d’installation", "इंस्टॉल फ़ोल्डर");

    public static string Browse => T("Browse…", "Обзор…", "Огляд…", "Gözat…", "Ko‘rish…", "浏览…", "瀏覽…", "찾아보기…", "参照…", "Examinar…", "Durchsuchen…", "Parcourir…", "ब्राउज़…");

    public static string BrowseTitle => T("Demar VPN install folder", "Папка установки Demar VPN", "Папка встановлення Demar VPN", "Demar VPN kurulum klasörü", "Demar VPN o‘rnatish papkasi", "Demar VPN 安装文件夹", "Demar VPN 安裝資料夾", "Demar VPN 설치 폴더", "Demar VPN インストールフォルダー", "Carpeta de instalación de Demar VPN", "Demar-VPN-Installationsordner", "Dossier d’installation Demar VPN", "Demar VPN इंस्टॉल फ़ोल्डर");

    public static string Install => T("Install", "Установить", "Встановити", "Yükle", "O‘rnatish", "安装", "安裝", "설치", "インストール", "Instalar", "Installieren", "Installer", "इंस्टॉल");

    public static string Uninstall => T("Uninstall", "Удалить", "Видалити", "Kaldır", "O‘chirish", "卸载", "解除安裝", "제거", "アンインストール", "Desinstalar", "Deinstallieren", "Désinstaller", "अनइंस्टॉल");

    public static string Open => T("Open", "Открыть", "Відкрити", "Aç", "Ochish", "打开", "開啟", "열기", "開く", "Abrir", "Öffnen", "Ouvrir", "खोलें");

    public static string Installed => T("Installed", "Установлено", "Встановлено", "Yüklendi", "O‘rnatildi", "已安装", "已安裝", "설치됨", "インストール済み", "Instalado", "Installiert", "Installé", "इंस्टॉल हो गया");

    public static string Uninstalled => T("Uninstalled", "Удалено", "Видалено", "Kaldırıldı", "O‘chirildi", "已卸载", "已解除安裝", "제거됨", "アンインストール済み", "Desinstalado", "Deinstalliert", "Désinstallé", "अनइंस्टॉल हो गया");

    public static string Error => T("Error", "Ошибка", "Помилка", "Hata", "Xato", "错误", "錯誤", "오류", "エラー", "Error", "Fehler", "Erreur", "त्रुटि");

    public static string Stopping => T("Stopping processes…", "Остановка процессов…", "Зупинка процесів…", "İşlemler durduruluyor…", "Jarayonlar to‘xtatilmoqda…", "正在停止进程…", "正在停止處理程序…", "프로세스 중지 중…", "プロセスを停止しています…", "Deteniendo procesos…", "Prozesse werden beendet…", "Arrêt des processus…", "प्रक्रियाएँ रोकी जा रही हैं…");

    public static string Cleaning => T("Cleaning old files…", "Очистка старых файлов…", "Очищення старих файлів…", "Eski dosyalar temizleniyor…", "Eski fayllar tozalanmoqda…", "正在清理旧文件…", "正在清理舊檔案…", "이전 파일 정리 중…", "古いファイルをクリーンアップしています…", "Limpiando archivos antiguos…", "Alte Dateien werden bereinigt…", "Nettoyage des anciens fichiers…", "पुरानी फ़ाइलें साफ़ हो रही हैं…");

    public static string Extracting => T("Extracting…", "Распаковка…", "Розпакування…", "Ayıklanıyor…", "Ochilmoqda…", "正在解压…", "正在解壓縮…", "압축 해제 중…", "展開しています…", "Extrayendo…", "Wird extrahiert…", "Extraction…", "निकाला जा रहा है…");

    public static string Registering => T("Registering…", "Регистрация…", "Реєстрація…", "Kaydediliyor…", "Ro‘yxatga olinmoqda…", "正在注册…", "正在註冊…", "등록 중…", "登録しています…", "Registrando…", "Wird registriert…", "Enregistrement…", "पंजीकरण हो रहा है…");

    public static string Done => T("Done", "Готово", "Готово", "Tamam", "Tayyor", "完成", "完成", "완료", "完了", "Listo", "Fertig", "Terminé", "हो गया");

    public static string Removing => T("Removing…", "Удаление…", "Видалення…", "Kaldırılıyor…", "O‘chirilmoqda…", "正在删除…", "正在移除…", "제거 중…", "削除しています…", "Eliminando…", "Wird entfernt…", "Suppression…", "हटाया जा रहा है…");

    public static string Finishing => T("Finishing…", "Завершение…", "Завершення…", "Tamamlanıyor…", "Yakunlanmoqda…", "正在完成…", "正在完成…", "마무리 중…", "完了処理中…", "Finalizando…", "Wird abgeschlossen…", "Finalisation…", "पूरा किया जा रहा है…");

    public static string EmptyInstallPath => T("Install path is empty", "Путь установки пуст", "Шляхх встановлення порожній", "Kurulum yolu boş", "O‘rnatish yo‘li bo‘sh", "安装路径为空", "安裝路徑為空", "설치 경로가 비어 있습니다", "インストールパスが空です", "La ruta de instalación está vacía", "Installationspfad ist leer", "Le chemin d’installation est vide", "इंस्टॉल पथ खाली है");
}
