**Demar VPN Windows** — нативный VPN-клиент на C# + WinUI 3

Платформа: **Windows only** (x64 unpackaged EXE). Android/iOS/macOS не в scope.

**Min OS:** Windows 10 19041+ (WinUI 3 / Windows App SDK).

Ядро: **sing-box latest** (**Hysteria2 + Trojan + VLESS Reality**). Документация: [https://sing-box.sagernet.org/](https://sing-box.sagernet.org/)

**UI:** Fluent Design, **тёмная тема only**, seed `#3A6EA5` — MaterialColorUtilities `TonalSpot`.

Структура: `filestree.md` · ядро: `readme-core.md`

---

## Первая сборка

Требуется **Windows** + VS 2022 / .NET 10 SDK (Desktop/WPF для Installer) + Windows App SDK.

Локально (не в git): `sing-box.exe` + `wintun.dll` + `libcronet.dll`, rule-sets, иконки/флаги. Release publish → `artifacts/Updater.exe`.

```powershell
dotnet build src/DemarVpn/DemarVpn.csproj -c Debug
```

Версия: `version.properties`.

Артефакты:

- `artifacts/Updater.exe` — установщик (WPF; внутри `payload.zip` + AOT `Uninstall.exe`; GUI / `/silent` / `/launch` / `/D=C:\Path` / `/uninstall`). Путь установки запоминается в `%LOCALAPPDATA%\DemarVpn\install_location` для обновлений.

После установки в выбранную папку (по умолчанию `Program Files\Demar VPN\`):

- `Demar VPN.exe` — приложение
- `Uninstall.exe` — крошечный AOT-деинсталлятор с Demar UI (извлекается из Updater)

Ярлык: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Demar VPN.lnk` (ПКМ → Удалить через ARP).

---

## UI

| Экран                         |                                                                                                                             |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Home (VPN toggle)             | Connect/disconnect через `VpnController` (HY2 / Trojan / VLESS); выбранный сервер — flag + city + large badges → details sheet   |
| Servers                       | Title «Servers»; multi-select фильтры (sheet tier+protocol, chips stretch ≤3/ряд), badges, favorites, `ServerDetailsSheet` (Load/TCP/TLS/Traffic/Direct), `~tlsMs`+chevron / Premium; Filter FAB 32px над Refresh 64px |
| Settings + social links       | FA6 brand icons (`IconLinkChip`: X / Bluesky / Threads / Telegram); промокод (`POST fastapi…/vpn/promo/activate` → refresh account+servers); Documents sheet → ToS/PP с GitHub Markdown; **Support** тикет-чат (`csauth.example.invalid`, `channel=windows`, альбом до 10 фото WebP, unread badge) |
| Account + login + notify/traffic/info sheets | Email OTP + TOTP + Telegram OAuth → `auth.example.invalid`; Account: подписка / **checkout** (narrow≈iOS / wide≈macOS) / трафик pie / **SubscriptionInfo** (expiry / freeTraffic) / семья (stub) / уведомления / выход; авто-алерты `SubscriptionAlertStore`; `AccountStore` refresh при старте и при обновлении серверов (`fastapi…/vpn/account|traffic|notify`); оплата: `GET /vpn/pay/plans` → `POST /vpn/pay/create` → браузер → `demarvpn://pay/ok|fail` |
| App settings (язык + журнал + beta + autostart) | **Language** 13 langs via `Locales/{En,Ru,…}.cs` (Apple/Android-style EN-key maps) + `Strings.L`; first launch = OS if supported, иначе EN; remount UI; checkout **ru→RUB**, else **EUR**; `AppLog` / beta / autostart / updates; **TUN / Routing**: Ad block (bundled local ads `.srs`, DNS+route reject, Free off / Plus·VIP default on) + custom apps (`.exe`) + **custom sites** (URL→host, Enter/CSV) + API app/site groups; geo bypass = bundled geosite/geoip `.srs` (no runtime download) |
| Service menu (3× tap version) | English only — About, Route, egress probes, device identity, **Info sheets** (expiry / freeTraffic / traffic / pay result) |
| Installer / Uninstall         | UI по языку ОС (en/ru/uk/tr/uz/zh-Hans/zh-Hant/ko/ja/es/de/fr/hi; иначе EN); без переключателя |


VPN: `SingBoxProcessEngine` + Wintun (HY2 / Trojan / VLESS). При Connect — **UAC для sing-box.exe** (TUN).

### Auth

Email OTP + TOTP (если включён) и Telegram OIDC через браузер → `demarvpn://auth/callback`. Сессия в `%LOCALAPPDATA%\DemarVpn\demar_auth_session.json`; при старте — `session/check`, при отзыве — local sign-out. Выход: `POST /auth/mobile/session/logout` + stop VPN + очистка `last_profile_id`. Новый email: confirm «Создать новый?» → `purpose: register`. API: `POST /auth/mobile/`* + `X-Demar-Client` / UA `DemarVPN/… (windows)`.

### Servers

После входа `POST https://vpn.example.invalid/vpn/servers` → список по подписке. Session cache: `%LOCALAPPDATA%\DemarVpn\session_cache.json` (hydrate до сети). Refresh = API; ping (`ServerReachProbe`) в фоне. Неподдерживаемые протоколы пропускаются. Без сессии — пустой список.

---

## Отладка

Журнал: `%LOCALAPPDATA%\DemarVpn\demar\demar_app.log` (tail-trim 512 строк, parity с Android `AppLog`). UI: «Настройки → Настройки приложения».

Unit-тесты pipeline (без WinUI): `dotnet test tests/DemarVpn.Tests`

