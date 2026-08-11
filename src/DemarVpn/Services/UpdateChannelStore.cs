namespace DemarVpn.Services;

/// <summary>prefer_beta preference — parity Android UpdateChannelStore.</summary>
public static class UpdateChannelStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "prefer_beta");

    static int _preferBeta; // 0/1 for Interlocked

    public static bool PreferBeta => Volatile.Read(ref _preferBeta) != 0;

    public static void Init()
    {
        try
        {
            if (File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() is "1" or "true")
                Volatile.Write(ref _preferBeta, 1);
        }
        catch { /* ignore */ }
    }

    public static void Set(bool preferBeta)
    {
        Volatile.Write(ref _preferBeta, preferBeta ? 1 : 0);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, preferBeta ? "1" : "0");
        }
        catch { /* ignore */ }
    }
}
