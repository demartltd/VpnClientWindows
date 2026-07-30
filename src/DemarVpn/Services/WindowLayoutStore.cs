namespace DemarVpn.Services;

public static class WindowLayoutStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DemarVpn", "window_size");

    public static bool TryLoad(out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (!File.Exists(FilePath)) return false;
            var parts = File.ReadAllText(FilePath).Split(',');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out width)
                || !int.TryParse(parts[1], out height)
                || width <= 0 || height <= 0)
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(int width, int height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, $"{width},{height}");
        }
        catch { /* ignore */ }
    }
}
