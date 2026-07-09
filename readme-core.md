# Ядро VPN: Sing-box (NaiveProxy) — Demar VPN Windows

Целевая версия core: **sing-box latest**. Фактическая — в `src/DemarVpn/Assets/singbox/bundled_version.txt` после `scripts/for_builds/libbox-build-windows.py`.

Документация: https://sing-box.sagernet.org/

- [Naive outbound](https://sing-box.sagernet.org/configuration/outbound/naive/) (since 1.13.0)
- [DNS rule action](https://sing-box.sagernet.org/configuration/dns/rule_action/) (since 1.11.0)
- [Dial Fields](https://sing-box.sagernet.org/configuration/shared/dial/) — `domain_resolver`

Платформа: **Windows only** (WinUI 3 + sing-ffi C# bindings).

**Итерация 2:** C# integration — **реализовано** (`VpnController`, `SingBoxConfigBuilder`, process/Libbox engines).

---

## Pipeline

```
ServerProfile → SingBoxConnectPlan → [resolveUplinkIpv4s] → SingBoxConfigBuilder → config.json
```

`resolveUplinkIpv4s` — перед connect на Windows. Расширяемость: новый profile + `SingBoxConnectPlan.foo()` + `ServerProfileKind.foo`.

### Добавление протокола

1. Profile struct (`FooProfile`) в `Domain/Vpn/`
2. `SingBoxConnectPlan.foo(profile)` — outbound + tun overrides
3. `ServerProfileKind.foo` + switch в `connectPlan`
4. Test server seed
5. При новом outbound — обновить TAGS в `scripts/for_builds/libbox-build-windows.py`

---

## NaiveProxy

| App field | sing-box key |
|-----------|--------------|
| `server` | `server` |
| `port` | `server_port` |
| `username` | `username` |
| `password` | `password` |
| `tlsServerName` | `tls.server_name` |
| `displayName` | UI only |

### Outbound defaults (`SingBoxConnectPlan.naive`)

| Поле | Значение |
|------|----------|
| `udp_over_tcp` | `true` |
| `quic` | `false` |
| `tls` | enabled + `server_name` |
| `domain_resolver` | `dns-direct`, `ipv4_only` |

---

## DNS (sing-box 1.11+ rule actions)

| Tag | Тип | Назначение |
|-----|-----|------------|
| `dns-direct` | UDP `1.1.1.1` | bootstrap, domain resolver |
| `dns-remote` | HTTPS `1.1.1.1` | DoH через proxy |

Rules: PTR → reject; bootstrap domains → `dns-direct`. Глобально: `final: dns-remote`, `strategy: ipv4_only`.

---

## Route и TUN

- TUN: **Wintun** (Windows), `strict_route`, `172.19.0.1/30`, split `0.0.0.0/1` + `128.0.0.0/1`
- Route: sniff → hijack-dns → QUIC reject → private direct → IPv6 reject → proxy
- `default_domain_resolver`: `dns-direct`, `ipv4_only`

(Android использует gVisor TUN + `override_android_vpn`; на Windows — Wintun adapter.)

---

## Uplink bypass

| Uplink `server` | Механизм |
|-----------------|----------|
| Literal IPv4 | `route_exclude_address` в TUN |
| Hostname | `resolveUplinkIpv4s` → `withExtraDirectIps` перед build |

---

## Diag inbound

Mixed `127.0.0.1:10808` (`SingBoxTags.diagInbound`):

- Post-connect: `VpnTunnelProbe`
- Egress: `NetworkDiagnostics` (roadmap)
- Native snapshot: `getEgressRouteSnapshot`

---

## Сборка Libbox (Windows)

```powershell
py -3 scripts/for_builds/libbox-build-windows.py
pwsh scripts/for_builds/build_windows.ps1
```

Очистка: `python3 scripts/singbox-cleaner.py -y`

### Build (sing-ffi)

```makefile
lib_windows:
    sing-ffi generate --config ./experimental/libbox/ffi.json --platform-type csharp
```

Скрипт клонирует тег `v1.13.13`, генерирует C# в `src/DemarVpn/Native/libbox/`.

TAGS (parity с Android):

```
with_gvisor,with_naive_outbound,with_clash_api,badlinkname,tfogo_checklinkname0
```

---

## C# layout

| Путь | Роль |
|------|------|
| `Domain/Vpn/*` | Profiles, `SingBoxConnectPlan` |
| `Services/Vpn/SingBoxConfigBuilder.cs` | TUN + DNS + route JSON (`stack: system`) |
| `Services/Vpn/VpnController.cs` | Connect pipeline, UI bridge |
| `Services/Network/UplinkIpv4.cs` | uplink resolve |
| `Native/libbox/` | sing-ffi generated bindings |
| `Services/Vpn/LibboxVpnEngine.cs` | Libbox in-process (`HAS_LIBBOX`) |
| `Services/Vpn/SingBoxProcessEngine.cs` | sing-box.exe subprocess fallback |
| `Services/Vpn/WintunPlatform.cs` | PlatformInterface (`HAS_LIBBOX`) |

Синхронизировать TAGS / pipeline с [demar_vpn_android/readme-core.md](../demar_vpn_android/readme-core.md).
