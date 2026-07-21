namespace DemarVpn.Installer;

readonly record struct InstallerMode(bool Uninstall, bool Silent, string? InstallDir)
{
    public static InstallerMode Detect()
    {
        var exe = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Updater");
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var uninstall = exe.Equals("Uninstall", StringComparison.OrdinalIgnoreCase)
            || args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        var silent = args.Any(a =>
            a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/verysilent", StringComparison.OrdinalIgnoreCase));

        string? dir = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase))
                dir = a[3..].Trim('"');
            else if (a.Equals("/D", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                dir = args[++i].Trim('"');
        }

        return new(uninstall, silent, string.IsNullOrWhiteSpace(dir) ? null : dir);
    }
}
