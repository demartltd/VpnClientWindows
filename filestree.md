# Структура проекта

Windows-only. C# + WinUI 3 (dark only, x64 MSIX).

## `src/DemarVpn/`

```
src/DemarVpn/
├── App.xaml / MainWindow.xaml
├── Package.appxmanifest
├── Theme/                         # DemarPalette (MaterialColorUtilities TonalSpot)
├── Domain/Vpn/                    # NaiveProfile, SingBoxConnectPlan, ServerProfile, …
├── Views/
│   ├── ShellPage.xaml             # tabs + overlay frame + snackbar overlay
│   ├── HomePage.xaml
│   ├── ServersPage.xaml
│   ├── SettingsPage.xaml
│   ├── AccountPage.xaml
│   ├── AppSettingsPage.xaml
│   └── ServiceMenuPage.xaml
├── Controls/
│   ├── FloatingToolbar.xaml
│   ├── DemarSnackbarHost.xaml
│   ├── IconLinkChip.xaml
│   ├── DemarKvRow.xaml             # key/value row (service menu)
│   └── ServerRow.xaml             # флаг + displayName + country
├── Services/
│   ├── AppPackageInfo.cs
│   ├── Vpn/                       # VpnController, SingBoxConfigBuilder, engines
│   ├── Device/DeviceIdentity.cs
│   ├── Network/
│   │   ├── UplinkIpv4.cs
│   │   ├── PublicEgressDiagnostics.cs
│   │   └── WindowsEgressRouteSnapshot.cs
│   ├── AppLog.cs
│   └── SnackbarService.cs
├── Core/Servers/TestServer.cs
├── Models/
├── Assets/                        # MSIX icons, BrandIcons.xaml, Flags/, singbox/
└── Native/
    ├── libbox/                    # sing-ffi C# (gitignore)
    ├── packages/                  # SagerNet.Libbox nupkg (gitignore)
    ├── runtimes/win-x64/native/   # wintun.dll, sing-box.exe (gitignore)
    └── bin/                       # build output sing-box.exe (gitignore)
```

## Tests

```
tests/DemarVpn.Tests/
├── VpnPipelineTest.cs
└── AppLogTest.cs
```

## Scripts

```
scripts/
├── journal.ps1                    # AppLog journal (parity adb/journal.sh)
├── for_builds/
│   ├── libbox-build-windows.py
│   ├── build_windows.ps1
│   ├── ensure-dev-signing.ps1
│   ├── trust-dev-cert.ps1
│   └── verify-msix-signing.ps1
├── port_brand_icons.py
├── other/
│   └── port_country_flags.py
├── generate_icons.py
├── singbox-cleaner.py
├── install_release.ps1
├── increment_version*.ps1
├── open_output_folders.ps1
└── push-github.ps1
```

## Version

```
version.properties               # VERSION_NAME, VERSION_CODE
```

Синхронизация sing-box TAGS с `demar_vpn_android/scripts`.
