namespace DemarVpn.Services.Vpn;

public static class CoreWatchArgs
{
    public const string Flag = "--core-watch";

    public static bool TryParse(string[] args, out int parentPid, out string exe, out string[] coreArgv)
    {
        parentPid = 0;
        exe = "";
        coreArgv = [];

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], Flag, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out parentPid) || parentPid <= 0)
                return false;
            i += 2;
            if (i < args.Length && args[i] == "--") i++;
            if (i >= args.Length) return false;
            exe = args[i++];
            coreArgv = i < args.Length ? args[i..] : [];
            return !string.IsNullOrWhiteSpace(exe);
        }

        return false;
    }
}
