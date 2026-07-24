**Demar VPN Windows** — нативный VPN-клиент на C# + WinUI 3

Платформа: **Windows only** (x64 unpackaged EXE). Android/iOS/macOS не в scope.

**Min OS:** Windows 10 19041+ (WinUI 3 / Windows App SDK).

Ядро: **sing-box latest** (**Hysteria2**). Документация: https://sing-box.sagernet.org/

**UI:** Fluent Design, **тёмная тема only**, seed `#3A6EA5` — MaterialColorUtilities `TonalSpot` (parity с Android / Flutter).

Структура: `filestree.md` · ядро: `readme-core.md`

---

## Первая сборка

Требуется **Windows** + VS 2022 / .NET 10 SDK (Desktop/WPF для Installer) + Windows App SDK + Go (для Libbox).

```powershell
py -3 scripts/for_builds/libbox-build-windows.py   # sing-box.exe + wintun.dll + libcronet.dll
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
- `artifacts/Updater.exe` — установщик (WPF; внутри `payload.zip` + AOT `Uninstall.exe`; GUI / `/silent` / `/D=C:\Path` / `/uninstall`). Путь установки запоминается в `%LOCALAPPDATA%\DemarVpn\install_location` для обновлений.

После установки в выбранную папку (по умолчанию `Program Files\Demar VPN\`):
- `Demar VPN.exe` — приложение
- `Uninstall.exe` — крошечный AOT-деинсталлятор с Demar UI (извлекается из Updater)

Ярлык: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Demar VPN.lnk` (ПКМ → Удалить через ARP).

---

## UI (parity с demar_vpn Android)

| Экран | Статус |
|-------|--------|
| Home (VPN toggle) | Hysteria2 connect/disconnect через `VpnController` |
| Servers | SegmentBar протокол/тиры (drag), divider, `Country \| TIER` + city, FAB refresh, reachability; Dev/Tester только если есть в списке |
| Settings + social links | FA6 brand icons (`IconLinkChip`), реальные URL |
| Account + login dialog | Email OTP + TOTP + Telegram OAuth → `auth.example.invalid` |
| App settings (журнал) | `AppLog` — до 512 строк, tail-trim |
| Service menu (3× tap version) | О приложении, Route, egress probes, device identity |

VPN connect (Hysteria2) — **реализовано** (`SingBoxProcessEngine` + Wintun). При Connect появится **UAC для sing-box.exe** (TUN).

### Auth

Email OTP + TOTP (если включён) и Telegram OIDC через браузер → `demar://auth/callback`. Сессия в `%LOCALAPPDATA%\DemarVpn\demar_auth_session.json`; при старте — `session/check`, при отзыве — local sign-out. Новый email: confirm «Создать новый?» → `purpose: register`. API: `POST /auth/mobile/*` + `X-Demar-Client` / UA `DemarVPN/… (windows)`.

### Servers

После входа `POST /auth/mobile/servers` → список по подписке. UI: SegmentBar протокола + All/тиры (Dev/Tester только при наличии), divider, строки `Country | TIER` / `City #N`, floating refresh (roll + snackbar), DNS reachability-dot. Неподдерживаемые протоколы пропускаются. Без сессии — CTA войти.

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

### Сборка долго висит (>15 мин)

1. **Прервите** (Ctrl+C) и используйте быстрый режим: `pwsh scripts/for_builds/build_windows.ps1 -Fast`
2. **Windows Defender** — добавьте исключение для папки репозитория
3. Очистка: `dotnet clean DemarVpn.sln -c Release; Remove-Item -Recurse -Force src\DemarVpn\bin, src\DemarVpn\obj, artifacts`
4. В Диспетчере задач проверьте, не зависли ли `XamlCompiler.exe`, `dotnet.exe`

---

## Статус (2026-07)

| Область | Статус |
|---------|--------|
| WinUI 3 UI (4 tabs + overlay + login) | реализовано |
| Тема / snackbar / соц. иконки | parity с Android |
| Libbox VPN layer | `Native/libbox/` optional; **primary:** `SingBoxProcessEngine` |
| Domain / data / Hysteria2 pipeline | `Domain/Vpn/*`, `SingBoxConfigBuilder`, `UplinkProbe` |
| Auth + servers API | `Services/Auth/*`, `Services/Servers/*` → `auth.example.invalid` |
| Home connect/disconnect | `VpnController` + tunnel probe |
