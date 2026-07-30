# Структура проекта

Windows-only. C# + WinUI 3 (dark only, x64 unpackaged EXE).

## `src/DemarVpn/`

```
src/DemarVpn/
├── App.xaml / MainWindow.xaml
├── Theme/                         # DemarPalette (MaterialColorUtilities TonalSpot)
├── Domain/Vpn/                    # Hysteria2Profile, SingBoxConnectPlan, ServerProfile, ServerReachability
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
│   ├── LoginBottomSheet.xaml      # email OTP / TOTP / Telegram OAuth
│   ├── DemarSegmentBar.xaml       # protocol/tier filter (drag-scrub)
│   └── ServerRow.xaml             # флаг + listTitle/subtitle + reachability dot
├── Services/
│   ├── AppPackageInfo.cs
│   ├── Auth/                      # AuthApi, AuthRepository, AuthSessionStore, DemarHttp
│   ├── Servers/                   # ServersApi (POST /auth/mobile/servers → hy2)
│   ├── Vpn/                       # VpnController, SingBoxConfigBuilder, engines
│   ├── Device/DeviceIdentity.cs
│   ├── Network/
│   │   ├── UplinkIpv4.cs
│   │   ├── ServerReachProbe.cs    # DNS reachability for servers list
│   │   ├── PublicEgressDiagnostics.cs
│   │   └── WindowsEgressRouteSnapshot.cs
│   ├── AppLog.cs
│   ├── ProtocolRegistration.cs    # demar:// HKCU
│   └── SnackbarService.cs
├── Models/
├── Assets/                        # icons, BrandIcons.xaml, Flags/, singbox/
└── Native/
    └── bin/                       # sing-box.exe, wintun.dll, libcronet.dll (gitignore)
```

## Tests

```
tests/DemarVpn.Tests/
├── VpnPipelineTest.cs
├── UplinkProbeTest.cs
└── AppLogTest.cs
```

## `src/DemarVpn.Installer/`

```
src/DemarVpn.Installer/
├── DemarVpn.Installer.csproj   # EmbedPayload=true → Updater WPF; false → Uninstall AOT
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── InstallerMode.cs            # /silent /uninstall /D=
├── InstallService.cs           # install / uninstall / ARP / shortcut / demar://
├── UninstallProgram.cs         # Native AOT Win32 Demar UI
├── app.manifest                # requireAdministrator
└── Resources/                  # payload.zip + uninstall.exe — generated (gitignore)
```

Один `Updater.exe`: встроенные `payload.zip` + `uninstall.exe`.

## Scripts

```
scripts/
├── journal.ps1                    # AppLog journal (parity adb/journal.sh)
├── for_builds/
│   ├── singbox-build-windows.py
│   └── build_windows.ps1          # publish app + Updater.exe
├── port_brand_icons.py
├── other/
│   └── port_country_flags.py
├── generate_icons.py
├── singbox-cleaner.py
├── install_release.ps1            # silent Updater /silent
├── increment_version*.ps1
└── open_output_folders.ps1
```

## Version

```
version.properties               # VERSION_NAME, VERSION_CODE
```

Синхронизация sing-box TAGS с `demar_vpn_android/scripts`.
