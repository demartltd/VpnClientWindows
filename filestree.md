# Структура проекта

Windows-only. C# + WinUI 3 (dark only, x64 unpackaged EXE).

## `src/DemarVpn/`

```
src/DemarVpn/
├── App.xaml / MainWindow.xaml
├── Theme/                         # DemarPalette (MaterialColorUtilities TonalSpot)
├── Domain/
│   ├── Vpn/                       # Hysteria2Profile, SingBoxConnectPlan, ApiBypassRuleSet, ServerProfile, ServerReachability
│   └── Content/LegalDocument.cs   # Terms / Privacy + GitHub MD URLs
├── Views/
│   ├── ShellPage.xaml             # tabs + overlay frame + snackbar overlay
│   ├── HomePage.xaml
│   ├── ServersPage.xaml
│   ├── SettingsPage.xaml
│   ├── AccountPage.xaml
│   ├── AppSettingsPage.xaml
│   ├── SupportPage.xaml           # ticket list + chat overlay (csapi)
│   ├── DocumentPage.xaml          # ToS / PP Markdown viewer
│   └── ServiceMenuPage.xaml
├── Controls/
│   ├── FloatingToolbar.xaml
│   ├── OverlayChrome.xaml         # overlay ← + title + body slot
│   ├── BottomSheetChrome.xaml     # scrim + handle + body slot (all sheets)
│   ├── DemarSnackbarHost.xaml
│   ├── IconLinkChip.xaml
│   ├── DemarToggle.xaml           # compact ToggleSwitch (track only, right-aligned rows)
│   ├── DemarKvRow.xaml             # key/value row (service menu)
│   ├── LoginBottomSheet.xaml      # email OTP / TOTP / Telegram OAuth
│   ├── DocumentsBottomSheet.xaml  # Terms / Privacy picker
│   ├── NotifyBottomSheet.xaml     # account notify toggles
│   ├── TrafficBottomSheet.xaml    # monthly traffic pie
│   ├── SubscriptionCheckoutSheet.xaml  # pay checkout (narrow≈iOS / wide≈macOS @ 800)
│   ├── PlanCompareSheet.xaml      # Free/Plus/VIP compare
│   ├── PayResultSheet.xaml        # pay/ok|fail result
│   ├── TrafficPieChart.xaml
│   ├── DemarSegmentBar.xaml       # protocol/tier filter (drag-scrub)
│   ├── SettingsActionButton.xaml
│   ├── SupportChatPane.xaml       # support chat composer + messages
│   ├── SupportBubble.xaml         # message bubble + media
│   └── ServerRow.xaml             # флаг + listTitle/subtitle + reachability dot
├── Services/
│   ├── AppPackageInfo.cs
│   ├── AppUpdateService.cs        # fastapi version+sha256, download /windows/download|beta
│   ├── WindowsReleaseApi.cs       # GET /windows/version (+ beta channel pick)
│   ├── UpdateChannelStore.cs      # prefer_beta prefs
│   ├── LocaleStore.cs             # UI language en|ru (first launch = OS)
│   ├── RunAtStartup.cs            # logon Task Scheduler autostart (default ON)
│   ├── Account/                   # VpnAccountApi, AccountStore, PayApi (plans/create + promo/apply)
│   ├── Support/                   # SupportApi → csauth.example.invalid ticket chat (channel=windows; album ≤10 WebP)
│   ├── Auth/                      # AuthApi, AuthRepository, AuthSessionStore, DemarHttp
│   ├── Servers/                   # ServersApi, SessionCacheStore (servers/account/traffic/tun), SessionRefreshPolicy
│   ├── Content/                   # LegalDocumentRepository, RawTextFetcher, MarkdownBlocks
│   ├── Vpn/                       # VpnController, SingBoxConfigBuilder, engines
│   ├── Device/DeviceIdentity.cs
│   ├── Network/
│   │   ├── UplinkIpv4.cs
│   │   ├── ServerReachProbe.cs    # DNS reachability for servers list
│   │   ├── PublicEgressDiagnostics.cs
│   │   └── WindowsEgressRouteSnapshot.cs
│   ├── AppLog.cs
│   ├── DemarNavigation.cs         # demarvpn:// auth + support + pay + account deep links
│   ├── ProtocolRegistration.cs    # demarvpn:// HKCU
│   └── SnackbarService.cs
├── Models/
├── Assets/                        # AppIcon.ico, logos, BrandIcons.xaml, Flags/, singbox/
└── Native/
    └── bin/                       # sing-box.exe, wintun.dll, libcronet.dll (gitignore)
```

## Tests

```
tests/DemarVpn.Tests/
├── VpnPipelineTest.cs
├── UplinkProbeTest.cs
├── AppLogTest.cs
├── AppUpdateTest.cs             # version parse + SHA256 + beta channel
├── RunAtStartupTest.cs          # run_at_startup prefs (default ON)
├── LocaleStoreTest.cs           # first-launch en/ru resolve
├── VpnAccountApiTest.cs         # account/traffic parse + promo + pieSlices + formatBytes
├── PayApiParseTest.cs           # pay catalog/checkout/formatPrice + CheckoutIntent
├── SupportApiTest.cs            # support ticket JSON parse / encode cache
├── SessionRefreshPolicyTest.cs  # session TTL 600s
├── SessionCacheStoreTest.cs     # session disk cache round-trip (servers/account/traffic/tun)
├── DemarNavigationTest.cs       # demarvpn:// auth + support + pay + account deep links
└── LegalDocumentTest.cs         # ToS/PP URLs + RawTextFetcher.Decode
```

## `src/DemarVpn.Installer/`

```
src/DemarVpn.Installer/
├── DemarVpn.Installer.csproj   # EmbedPayload=true → Updater WPF; false → Uninstall AOT
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── InstallerMode.cs            # /silent /uninstall /launch /D=
├── InstallService.cs           # install / uninstall / ARP / shortcut / demarvpn://
├── InstallerStrings.cs         # EN/RU by OS UI culture
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
├── generate_icons.py              # Assets + AppIcon.ico из logo_clear.png
├── logo_clear.png                 # source (parity Android)
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
