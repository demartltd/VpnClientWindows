**Demar VPN Windows** — нативный VPN-клиент на C# + WinUI 3

Платформа: **Windows only** (x64 MSIX). Android/iOS/macOS не в scope.

**Min OS:** Windows 10 19041+ (WinUI 3 / Windows App SDK).

Ядро: **sing-box latest** (MVP: **NaiveProxy**). Документация: https://sing-box.sagernet.org/

**UI:** Fluent Design, **тёмная тема only**, seed `#3A6EA5` — MaterialColorUtilities `TonalSpot` (parity с Android / Flutter).

Структура: `filestree.md` · ядро: `readme-core.md`

---

## Первая сборка

Требуется **Windows** + VS 2022 / .NET 10 SDK + Windows App SDK + Go (для Libbox).

```powershell
py -3 scripts/for_builds/libbox-build-windows.py   # sing-box.exe + wintun.dll + libcronet.dll
python3 scripts/port_brand_icons.py       # при изменении Android brand icons
python3 scripts/other/port_country_flags.py  # Twemoji флаги → Assets/Flags/
python3 scripts/generate_icons.py         # Assets из scripts/logo_clear.png
pwsh scripts/for_builds/build_windows.ps1            # MSIX (~3–8 мин первая сборка)
pwsh scripts/install_release.ps1          # доверие сертификату + установка MSIX
```

**Быстрая разработка без MSIX** (не создаёт `.msix`):

```powershell
pwsh scripts/for_builds/build_windows.ps1 -Fast
```

Dev-сертификат MSIX (один раз, автоматически при сборке): `pwsh scripts/for_builds/ensure-dev-signing.ps1`  
Диагностика подписи: `pwsh scripts/for_builds/verify-msix-signing.ps1`

Версия: `version.properties`. Bump: `pwsh scripts/increment_version_auto.ps1`

---

## MSIX: установка и регистрация разработчика

### Локальная разработка (бесплатно)

Регистрация **не нужна**. Self-signed PFX + доверие сертификата на машине.

1. Включите **Режим разработчика**: Параметры → Конфиденциальность и защита → Для разработчиков.
2. Соберите и установите **только через скрипты** (не двойным кликом по `.msix`):

```powershell
pwsh scripts/for_builds/ensure-dev-signing.ps1
pwsh scripts/for_builds/build_windows.ps1          # не -Fast
pwsh scripts/install_release.ps1        # UAC для certutil
```

После смены машины или thumbprint mismatch: `pwsh scripts/for_builds/ensure-dev-signing.ps1 -Recreate`

### Microsoft Store (публичное распространение)

1. [Microsoft Partner Center](https://partner.microsoft.com/dashboard) — регистрация аккаунта разработчика (Individual ~$19 one-time).
2. Создать приложение → загрузить MSIX, подписанный Store certificate.
3. Пользователям не нужно доверять сертификату вручную.

### Распространение вне Store (production)

Купить **Code Signing Certificate** (OV/EV) у CA: DigiCert, SSL.com, Sectigo. В build заменить `PackageCertificateKeyFile` и `Publisher` в `Package.appxmanifest` на CN из сертификата CA.


---

## UI (parity с demar_vpn Android)

| Экран | Статус |
|-------|--------|
| Home (VPN toggle) | Naive connect/disconnect через `VpnController` |
| Servers | FI-DEV-99, SE-FREE-71, GR-FREE-22, DE-FREE-1, флаги из тега (`TestServer.cs`) |
| Settings + social links | FA6 brand icons (`IconLinkChip`), реальные URL |
| Account + login dialog | UI steps без API |
| App settings (журнал) | `AppLog` — до 512 строк, tail-trim |
| Service menu (3× tap version) | О приложении, Route, egress probes, device identity |

VPN connect (Naive MVP) — **реализовано** (`SingBoxProcessEngine` + Wintun). MSIX не может запускаться от администратора — при Connect появится **UAC для sing-box.exe** (TUN).

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
2. **Не используйте `dotnet publish`** для MSIX — скрипт теперь вызывает `dotnet build` + `GenerateAppxPackageOnBuild`
3. **Windows Defender** — добавьте исключение для папки репозитория (частая причина 20+ мин на MakePri/pack)
4. Очистка: `dotnet clean DemarVpn.sln -c Release; Remove-Item -Recurse -Force src\DemarVpn\bin, src\DemarVpn\obj`
5. В Диспетчере задач проверьте, не зависли ли `MakePri.exe`, `XamlCompiler.exe`, `dotnet.exe`

### MSIX: 0x800B0109 / 0x800B010A (сертификат)

Ошибка = корневой сертификат не доверен. **Не ставьте MSIX двойным кликом.** Нужен `LocalMachine\Root` (UAC).

```powershell
pwsh scripts/for_builds/verify-msix-signing.ps1
pwsh scripts/for_builds/trust-dev-cert.ps1          # UAC для LocalMachine\Root
pwsh scripts/install_release.ps1
```

---

## Статус (2026-06)

| Область | Статус |
|---------|--------|
| WinUI 3 UI (4 tabs + overlay + login) | реализовано |
| Тема / snackbar / соц. иконки | parity с Android |
| Libbox VPN layer | `Native/libbox/` optional; **primary:** `SingBoxProcessEngine` |
| Domain / data / Naive pipeline | `Domain/Vpn/*`, `SingBoxConfigBuilder`, `UplinkProbe` |
| Home connect/disconnect | `VpnController` + tunnel probe |
