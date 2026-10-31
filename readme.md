**Demar VPN Windows** — native VPN client in C# + WinUI 3

Platform: **Windows only** (x64 unpackaged EXE). Android/iOS/macOS are out of scope.

**Min OS:** Windows 10 19041+ (WinUI 3 / Windows App SDK).

Core: **sing-box latest** (**Hysteria2 + Trojan + VLESS Reality**). Docs: https://sing-box.sagernet.org/

**UI:** Fluent Design, **dark theme only**, seed `#3A6EA5` — MaterialColorUtilities `TonalSpot`.

Layout: `filestree.md` · core: `readme-core.md`

---

## First build

Requires **Windows** + VS 2022 / .NET 10 SDK (Desktop/WPF for Installer) + Windows App SDK.

Prepare locally (not in git): copy `.env.example` → `.env`, `sing-box.exe` + `wintun.dll` + `libcronet.dll`, rule-sets, icons/flags. Release publish → `artifacts/Updater.exe`.

```powershell
dotnet build src/DemarVpn/DemarVpn.csproj -c Debug
```

Version: `version.properties`.

Artifacts:

- `artifacts/Updater.exe` — installer (WPF; embeds `payload.zip` + AOT `Uninstall.exe`; GUI / `/silent` / `/launch` / `/D=C:\Path` / `/uninstall`). Install path stored in `%LOCALAPPDATA%\DemarVpn\install_location` for updates.

After install into the chosen folder (default `Program Files\Demar VPN\`):

- `Demar VPN.exe` — app
- `Uninstall.exe` — small AOT uninstaller with Demar UI (extracted from Updater)

Shortcut: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Demar VPN.lnk` (right-click → Uninstall via ARP).

---

## UI

| Screen | |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Home (VPN toggle)             | Connect/disconnect via `VpnController` (HY2 / Trojan / VLESS); selected server — flag + city + large badges → details sheet   |
| Servers                       | Title «Servers»; multi-select filters (sheet tier+protocol, chips stretch ≤3/row), badges, favorites, `ServerDetailsSheet` (Load/TCP/TLS/Traffic/Direct), `~tlsMs`+chevron / Premium; Filter FAB 32px above Refresh 64px |
| Settings + social links       | FA6 brand icons (`IconLinkChip`: X / Bluesky / Threads / Telegram); promo (`POST /vpn/promo/activate` → refresh account+servers); Documents → ToS/PP from GitHub Markdown; **Support** ticket chat (support API, `channel=windows`, up to 10 WebP photos, unread badge) |
| Account + login + notify/traffic/info sheets | Email OTP + TOTP + Telegram OAuth → auth API; Account: subscription / **checkout** (narrow≈iOS / wide≈macOS) / traffic pie / **SubscriptionInfo** (expiry / freeTraffic) / family (stub) / notifications / sign-out; auto-alerts `SubscriptionAlertStore`; `AccountStore` refresh on launch and when servers refresh (`/vpn/account|traffic|notify`); pay: `GET /vpn/pay/plans` → `POST /vpn/pay/create` → browser → `demarvpn://pay/ok|fail` |
| App settings (language + journal + beta + autostart) | **Language** 13 langs via `Locales/{En,Ru,…}.cs` (EN-key maps) + `Strings.L`; first launch = OS if supported, else EN; remount UI; checkout **ru→RUB**, else **EUR**; `AppLog` / beta / autostart / updates; **TUN / Routing**: Ad block (bundled local ads `.srs`, DNS+route reject, Free off / Plus·VIP default on) + custom apps (`.exe`) + **custom sites** (URL→host, Enter/CSV) + API app/site groups; geo bypass = bundled geosite/geoip `.srs` (no runtime download) |
| Service menu (3× tap version) | English only — About, Route, egress probes, device identity, **Info sheets** (expiry / freeTraffic / traffic / pay result) |
| Installer / Uninstall         | UI follows OS language (en/ru/uk/tr/uz/zh-Hans/zh-Hant/ko/ja/es/de/fr/hi; else EN); no in-app language switch |


VPN: `SingBoxProcessEngine` + Wintun (HY2 / Trojan / VLESS). On Connect — **UAC for sing-box.exe** (TUN).

### Auth

Email OTP + TOTP (when enabled) and Telegram OIDC via browser → `demarvpn://auth/callback`. Session in `%LOCALAPPDATA%\DemarVpn\demar_auth_session.json`; on launch — `session/check`; on revoke — local sign-out. Sign-out: `POST /auth/mobile/session/logout` + stop VPN + clear `last_profile_id`. New email: confirm «Create new?» → `purpose: register`. API: `POST /auth/mobile/*` + `X-Demar-Client` / UA `DemarVPN/… (windows)`.

### Servers

After sign-in `POST /vpn/servers` → subscription list. Session cache: `%LOCALAPPDATA%\DemarVpn\session_cache.json` (hydrate before network). Refresh = API; ping (`ServerReachProbe`) in background. Unsupported protocols are skipped. Without a session — empty list.

---

## Debug

Journal: `%LOCALAPPDATA%\DemarVpn\demar\demar_app.log` (tail-trim 512 lines, same idea as Android `AppLog`). UI: **Settings → App settings**.

Pipeline unit tests (no WinUI): `dotnet test tests/DemarVpn.Tests`
