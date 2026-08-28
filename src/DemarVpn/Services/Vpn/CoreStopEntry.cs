namespace DemarVpn.Services.Vpn;

public static class CoreStopEntry
{
    public const string Flag = "--core-stop";

    public static bool TryRunIfRequested()
    {
        if (!Environment.GetCommandLineArgs().Any(a =>
                string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)))
            return false;

        CoreProcess.KillAll();
        WintunCleanup.RemoveAll();

        Environment.Exit(0);
        return true;
    }
}
