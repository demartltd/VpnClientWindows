**Demar VPN Windows** — нативный VPN-клиент на C# + WinUI 3

Платформа: **Windows only** (x64 unpackaged EXE). Android/iOS/macOS не в scope.

**Min OS:** Windows 10 19041+ (WinUI 3 / Windows App SDK).

Ядро: **sing-box latest** (**Hysteria2**). Документация: [https://sing-box.sagernet.org/](https://sing-box.sagernet.org/)

**UI:** Fluent Design, **тёмная тема only**, seed `#3A6EA5` — MaterialColorUtilities `TonalSpot`.

Структура: `filestree.md` · ядро: `readme-core.md`

---

## Первая сборка

Требуется **Windows** + VS 2022 / .NET 10 SDK (Desktop/WPF для Installer) + Windows App SDK.

```powershell
py -3 scripts/for_builds/singbox-build-windows.py   # sing-box.exe + wintun.dll + libcronet.dll
python3 scripts/port_brand_icons.py       # при изменении Android brand icons
python3 scripts/other/port_country_flags.py  # Twemoji флаги → Assets/Flags/
python3 scripts/generate_icons.py         # Assets из scripts/logo_clear.png
pwsh scripts/for_builds/build_windows.ps1            # Updater.exe → artifacts/
pwsh scripts/install_release.ps1          # silent install в Program Files
```

**Быстрая разработка** (без publish / Updater):

```powershell
pwsh scripts/for_builds/build_windows.ps1 -Fast
```

Версия: `version.properties`. Bump: `pwsh scripts/increment_version_auto.ps1`

Артефакты:

- `artifacts/Updater.exe` — установщик (WPF; внутри `payload.zip` + AOT `Uninstall.exe`; GUI / `/silent` / `/launch` / `/D=C:\Path` / `/uninstall`). Путь установки запоминается в `%LOCALAPPDATA%\DemarVpn\install_location` для обновлений.

После установки в выбранную папку (по умолчанию `Program Files\Demar VPN\`):

- `Demar VPN.exe` — приложение
- `Uninstall.exe` — крошечный AOT-деинсталлятор с Demar UI (извлекается из Updater)

Ярлык: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Demar VPN.lnk` (ПКМ → Удалить через ARP).

---

## UI (parity с demar_vpn Android)


| Экран                         | Статус                                                                                                                             |
| ----------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| Home (VPN toggle)             | Hysteria2 connect/disconnect через `VpnController`                                                                                 |
| Servers                       | SegmentBar протокол/тиры (drag), divider, `Country | TIER` + city, FAB refresh, reachability; Dev/Tester только если есть в списке |
| Settings + social links       | FA6 brand icons (`IconLinkChip`: X / Bluesky / IG / TikTok / Telegram); промокод (`POST fastapi…/vpn/promo/activate` → refresh account+servers); Documents sheet → ToS/PP с GitHub Markdown; **Support** тикет-чат (`csauth.example.invalid`, `channel=windows`, альбом до 10 фото WebP, unread badge) |
| Account + login + notify/traffic sheets | Email OTP + TOTP + Telegram OAuth → `auth.example.invalid`; Account: подписка / **checkout** (narrow≈iOS / wide≈macOS) / трафик pie / семья (stub) / уведомления / выход; `AccountStore` refresh при старте и при обновлении серверов (`fastapi…/vpn/account|traffic|notify`); оплата: `GET /vpn/pay/plans` → `POST /vpn/pay/create` → браузер → `demarvpn://pay/ok|fail` |
| App settings (язык + журнал + beta + autostart) | **Language** 13 langs via `Locales/{En,Ru,…}.cs` (Apple/Android-style EN-key maps) + `Strings.L`; first launch = OS if supported, иначе EN; remount UI; checkout **ru→RUB**, else **EUR**; `AppLog` / beta / autostart / updates; **TUN settings**: Ad block (DNS reject `geosite-category-ads`, Free off / Plus·VIP default on) + app/site bypass |
| Service menu (3× tap version) | English only — About, Route, egress probes, device identity |
| Installer / Uninstall         | UI по языку ОС (en/ru/uk/tr/uz/zh-Hans/zh-Hant/ko/ja/es/de/fr/hi; иначе EN); без переключателя |


VPN connect (Hysteria2) — **реализовано** (`SingBoxProcessEngine` + Wintun). При Connect появится **UAC для sing-box.exe** (TUN).

### Auth

Email OTP + TOTP (если включён) и Telegram OIDC через браузер → `demarvpn://auth/callback`. Сессия в `%LOCALAPPDATA%\DemarVpn\demar_auth_session.json`; при старте — `session/check`, при отзыве — local sign-out. Выход: `POST /auth/mobile/session/logout` + stop VPN + очистка `last_profile_id`. Новый email: confirm «Создать новый?» → `purpose: register`. API: `POST /auth/mobile/`* + `X-Demar-Client` / UA `DemarVPN/… (windows)`.

### Servers

После входа `POST https://vpn.example.invalid/vpn/servers` → список по подписке. Session cache (`%LOCALAPPDATA%\DemarVpn\session_cache.json`): servers + account + traffic + tun — hydrate при старте до сети (parity Apple). Один background loop раз в 10 мин: app update + session refresh if stale; connect без единого refresh в процессе — сначала `RefreshServersAsync`. UI: SegmentBar протокола + All/тиры (Dev/Tester только при наличии), divider, строки `Country | TIER` / `City #N`, floating refresh (roll + snackbar), DNS reachability-dot. Неподдерживаемые протоколы пропускаются. Без сессии — CTA войти.

---

## Отладка

```powershell
# Весь журнал в терминал (до 512 строк)
pwsh scripts/journal.ps1

# Сохранить в файл
pwsh scripts/journal.ps1 -OutFile journal.txt

# Последние 80 строк
pwsh scripts/journal.ps1 -Tail 80
```

Журнал: `%LOCALAPPDATA%\DemarVpn\demar\demar_app.log` (tail-trim 512 строк, parity с Android `AppLog`).

UI-журнал: «Настройки → Настройки приложения».

Unit-тесты pipeline (без WinUI): `dotnet test tests/DemarVpn.Tests`

