# Ядро VPN: Sing-box (Hysteria2) — Demar VPN Windows

Целевая версия core: **sing-box latest**. Фактическая — в `src/DemarVpn/Assets/singbox/bundled_version.txt` после `scripts/for_builds/singbox-build-windows.py`.

Документация: https://sing-box.sagernet.org/

- [Hysteria2 outbound](https://sing-box.sagernet.org/configuration/outbound/hysteria2/)
- [NaiveProxy outbound](https://sing-box.sagernet.org/configuration/outbound/naive/) (since 1.13; Windows → `libcronet.dll`)
- [DNS rule action](https://sing-box.sagernet.org/configuration/dns/rule_action/) (since 1.11.0)
- [Dial Fields](https://sing-box.sagernet.org/configuration/shared/dial/) — `domain_resolver`

Платформа: **Windows only** (WinUI 3 + sing-box.exe subprocess).

**Итерация:** C# integration — **реализовано** (`VpnController`, `SingBoxConfigBuilder`, `SingBoxProcessEngine`). Auth/servers — `auth.example.invalid` (parity Android).

Connect pipeline: `UplinkIpv4` (retry) → DNS resolve success → config → subprocess → `VpnTunnelProbe.WaitForTunnelReadyAsync` (diag IP ×2 + TUN hostname ×1). Hysteria2 = QUIC/UDP — **без TCP preflight**.

### Таймауты (`ConnectTimeouts`)

| Константа | Значение | Назначение |
|-----------|----------|------------|
| `PreflightMs` | 3 000 | TCP timeout (diag helpers) |
| `PreflightRaceMs` | 6 000 | parallel race budget |
| `StartupMs` | 4 000 | старт sing-box + diag-port 10808 |
| `ProxyWarmupMs` | 2 000 | handshake before proxy probe |
| `ProbeMs` | 15 000 | двухфазный probe (proxy + TUN/DNS) |
| `ConnectMs` | 40 000 | единый deadline pipeline |
| `WatchdogMs` | 42 000 | аварийный стоп при зависании в Connecting |
| `HealthIntervalMs` | 25 000 | post-connect health check |
| `HealthFailLimit` | 2 | подряд fail → auto Error |

### Смена сервера (`VpnController.Select`)

- **Disconnected**: только обновление `Selected`
- **Connected / Connecting / Error**: `SwitchServerAsync` — cancel → stop → connect к новому `Selected`
- Быстрые переключения: каждый `Select` отменяет предыдущую операцию через `_op.Cancel()`

---

## Pipeline

```
ServerProfile → SingBoxConnectPlan → [resolveUplinkIpv4s] → SingBoxConfigBuilder → config.json
```

`resolveUplinkIpv4s` — перед connect на Windows. Список серверов: `POST /auth/mobile/servers` → только `protocol=hysteria2`.

### Добавление протокола

1. Profile struct (`FooProfile`) в `Domain/Vpn/`
2. `SingBoxConnectPlan.Foo(profile)` — outbound + tun overrides
3. Parser в `ServersApi`
4. Official release zip уже включает Hysteria2 + Naive; `libcronet.dll` рядом с `sing-box.exe`

---

## Hysteria2

| App field | sing-box key |
|-----------|--------------|
| `server` | `server` |
| `port` | `server_port` |
| `password` | `password` |
| `tlsServerName` | `tls.server_name` |
| `alpn` | `tls.alpn` (default `["h3"]`) |
| `obfsPassword` | `obfs.type=salamander` + `obfs.password` |
| `displayName` | UI only |

### Outbound defaults (`SingBoxConnectPlan.Hysteria2`)

| Поле | Значение |
|------|----------|
| `tls` | enabled + `server_name` + `alpn` |
| `domain_resolver` | `dns-direct`, `ipv4_only` |
| `obfs` | optional salamander |

---

## DNS (sing-box 1.11+ rule actions)

| Tag | Тип | Назначение |
|-----|-----|------------|
| `dns-direct` | UDP `1.1.1.1` | bootstrap, domain resolver |
| `dns-remote` | HTTPS `1.1.1.1` | DoH через proxy |

Rules: PTR → reject; `localhost` → predefined (`127.0.0.1`); bootstrap domains (uplink hostname + TLS name) → `dns-direct`. Глобально: `final: dns-remote`, `strategy: ipv4_only`.

---

## Route и TUN

- TUN: **Wintun** (Windows), `strict_route`, `172.19.0.1/30`
- Split `0.0.0.0/1` + `128.0.0.0/1` — только при literal IPv4 uplink
- `route_exclude_address` **всегда**: `127.0.0.0/8`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local (`169.254/16`) + uplink IPs
- Route: sniff → hijack-dns → QUIC reject → `127.0.0.0/8` direct → private direct → IPv6 reject → proxy
- `default_domain_resolver`: `dns-direct`, `ipv4_only`

(Android использует gVisor TUN + `override_android_vpn`; на Windows — Wintun adapter.)

---

## Bypass

### Local bypass (всегда)

Loopback и LAN не проходят через TUN — напрямую на физический интерфейс. Use-case: Epic Games / Unreal Engine local servers (`127.0.0.1`).

| CIDR | Назначение |
|------|------------|
| `127.0.0.0/8` | loopback |
| `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` | RFC1918 LAN |
| `169.254.0.0/16` | link-local |

Дополнительно: route `127.0.0.0/8 → direct`, DNS `localhost → 127.0.0.1`.

### Uplink bypass (per-server)

| Uplink `server` | Механизм |
|-----------------|----------|
| Literal IPv4 | split routes + `{ip}/32` в `route_exclude_address` |
| Hostname | `resolveUplinkIpv4s` → `withExtraDirectIps` перед build |

---

## Diag inbound

Mixed `127.0.0.1:10808` (`SingBoxTags.diagInbound`):

- Post-connect: `VpnTunnelProbe` — Phase 1: diag-proxy IP trace ×2; Phase 2: TUN hostname trace ×1
- Post-connect health: `VpnController.HealthLoopAsync` (TUN trace каждые 25с)
- Egress: `PublicEgressDiagnostics` (Service Menu)

---

## Auth / Servers

| Endpoint | Назначение |
|----------|------------|
| `POST /auth/mobile/email/send\|confirm` | Email OTP |
| `POST /auth/mobile/totp/verify` | TOTP |
| `POST /auth/mobile/session/exchange\|check` | сессия |
| `POST /auth/mobile/servers` | список hy2 по подписке; UI: SegmentBar + listTitle/`City #N` + DNS reachability |
| `GET account.example.invalid/api/auth/client/me` | Telegram OAuth token |

Headers: `X-Demar-Client: demar-vpn`, `User-Agent: DemarVPN/{ver} (windows)`. Protocol: `demar://auth/callback`.

---

## Сборка ядра (Windows)

```powershell
py -3 scripts/for_builds/singbox-build-windows.py
# перекачать:  py -3 scripts/for_builds/singbox-build-windows.py --force
pwsh scripts/for_builds/build_windows.ps1
```

Runtime в `Native/bin/`: `sing-box.exe`, `wintun.dll`, `libcronet.dll` (NaiveProxy). Official release zip уже включает Hysteria2.

Очистка: `python3 scripts/singbox-cleaner.py -y`

---

## C# layout

| Путь | Роль |
|------|------|
| `Domain/Vpn/*` | Profiles, `SingBoxConnectPlan` |
| `Services/Auth/*` | API + session store |
| `Services/Servers/*` | servers fetch/parse |
| `Services/Vpn/SingBoxConfigBuilder.cs` | TUN + DNS + route JSON (`stack: system`) |
| `Services/Vpn/VpnController.cs` | Connect pipeline, UI bridge |
| `Services/Network/UplinkIpv4.cs` | uplink resolve |
| `Services/Vpn/SingBoxProcessEngine.cs` | sing-box.exe subprocess |

Синхронизировать TAGS / pipeline с [demar_vpn_android/readme-core.md](../demar_vpn_android/readme-core.md).
