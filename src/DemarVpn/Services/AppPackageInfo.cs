namespace DemarVpn.Services;

public static class AppPackageInfo
{
    public static string PackageId
    {
        get
        {
            try
            {
                var pkg = Windows.ApplicationModel.Package.Current;
                if (!string.IsNullOrEmpty(pkg?.Id.Name)) return pkg.Id.Name;
            }
            catch { /* unpackaged dev */ }
            return "Remader.DemarVPN";
        }
    }

    public static string VersionDetail
    {
        get
        {
            try
            {
                var v = Windows.ApplicationModel.Package.Current?.Id.Version;
                if (v is not null)
                    return $"{Strings.VersionName} ({v.Value.Major}.{v.Value.Minor}.{v.Value.Build}.{v.Value.Revision})";
            }
            catch { /* fallback */ }
            return Strings.VersionName;
        }
    }
}
