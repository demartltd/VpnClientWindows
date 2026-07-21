using System.Reflection;

namespace DemarVpn.Services;

public static class AppPackageInfo
{
    public static string PackageId => "Remader.DemarVPN";

    public static string VersionDetail
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null
                ? Strings.VersionName
                : $"{Strings.VersionName} ({v.Major}.{v.Minor}.{v.Build}.{v.Revision})";
        }
    }
}
