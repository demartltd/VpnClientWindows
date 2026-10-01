# Ядро VPN: Sing-box (Hysteria2) — Demar VPN Windows

Целевая версия core: **sing-box latest**. Фактическая — в `src/DemarVpn/Assets/singbox/bundled_version.txt` после `scripts/for_builds/singbox-build-windows.py`.

Документация: https://sing-box.sagernet.org/

- [Hysteria2 outbound](https://sing-box.sagernet.org/configuration/outbound/hysteria2/)
- [NaiveProxy outbound](https://sing-box.sagernet.org/configuration/outbound/naive/) (since 1.13; Windows → `libcronet.dll`)
- [DNS rule action](https://sing-box.sagernet.org/configuration/dns/rule_action/) (since 1.11.0)
- [Dial Fields](https://sing-box.sagernet.org/configuration/shared/dial/) — `domain_resolver`

Платформа: **Windows only** (WinUI 3 + sing-box.exe subprocess).

**Итерация:** C# integration — **реализовано** (`VpnController`, `SingBoxConfigBuilder`, `SingBoxProcessEngine`). Auth — `auth.example.invalid`; servers — `vpn.example.invalid` (parity Android).

Connect pipeline: `UplinkIpv4` (attempts=2) → DNS resolve success → config → subprocess (full stop before start) → diag `:10808` → `TunnelPathProbe` (poll CONNECT). Hysteria2 = QUIC/UDP — **без TCP preflight**.

### Таймауты (`ConnectTimeouts`)

| Константа | Значение | Назначение |
|-----------|----------|------------|
| `StartupMs` | 10 000 | старт sing-box + diag-port 10808 |
| `StartupPollMs` | 100 | poll diag |
| `ProbeMs` | 3 000 | budget CONNECT через diag после старта |
| `ProbeAttemptMs` | 900 | один attempt внутри ProbeMs |
| `ConnectMs` | 35 000 | единый deadline pipeline |
| `WatchdogMs` | 37 000 | аварийный стоп при зависании в Connecting |
| `HealthIntervalMs` | 25 000 | post-connect health (diag TCP) |
| `HealthFailLimit` | 2 | подряд fail → auto Error |
| `SwitchSettleMs` | 100 | пауза stop→connect при смене сервера |
| `DiagPortCloseMs` | 1 200 | ожидание закрытия diag при stop |

### Смена сервера (`VpnController.Select`)

- **Disconnected**: только обновление `Selected`
- **Connected / Connecting / Error**: coalesce switch (latest-wins) через serial `_ops` gate — cancel → полный stop → connect к текущему `Selected`
- Быстрые переключения: `_switchPending` + `_op.Cancel()`; overlapping Connect/Disconnect сериализуются
- `StartAsync` всегда делает полный stop (local + elevated/Wintun) перед новым стартом

---

## Pipeline

```
ServerProfile → SingBoxConnectPlan → [resolveUplinkIpv4s] → SingBoxConfigBuilder → config.json
```

`resolveUplinkIpv4s` — перед connect на Windows. Список серверов: `POST https://vpn.example.invalid/vpn/servers` → только `protocol=hysteria2`.

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
| `upMbps` / `downMbps` | `up_mbps` / `down_mbps` (empty → BBR) |
| `displayName` | UI only |

### Outbound defaults (`SingBoxConnectPlan.Hysteria2`)

| Поле | Значение |
|------|----------|
| `tls` | enabled + `server_name` + `alpn` |
| `udp_fragment` | `true` |
| `domain_resolver` | `dns-direct`, `ipv4_only` |
| `obfs` | optional salamander |
| `up_mbps` / `down_mbps` | omitted → BBR |

---

## DNS (sing-box 1.11+ rule actions)

| Tag | Тип | Назначение |
|-----|-----|------------|
| `dns-direct` | UDP `1.1.1.1` | bootstrap, domain resolver |
| `dns-remote` | HTTPS `1.1.1.1` | DoH через proxy |

Rules: PTR → reject; `localhost` → predefined (`127.0.0.1`); TUN process bypass → `dns-direct`; bootstrap domains (uplink hostname + TLS name) → `dns-direct`; site bypass hosts → `dns-direct`; **Ad block** (Plus/VIP, default on) → DNS `reject` by local `geosite-category-ads`; geosite rule-sets from API → `dns-direct`. Глобально: `final: dns-remote`, `strategy: ipv4_only`, `reverse_mapping: true` (parity Apple; IP→domain for route without FakeIP), `optimistic: true` (sing-box 1.14). `experimental.cache_file`: всегда `enabled` + `store_dns` (path inject → `%LOCALAPPDATA%\DemarVpn\demar\cache.db`).

### Ad block (DNS + route)

- Free: always off. Plus/VIP: default **on** (`tun_settings.json` `AdBlockEnabled` null → on); user can disable.
- Build: `scripts/for_builds/sync_ads_ruleset.ps1` → DustinWin `ads.srs` as `Assets/rule-sets/geosite-category-ads.srs` (gitignore; called from `build_windows.ps1`).
- Connect: bundled path → sing-box `type: local` / `format: binary` (parity Apple/Android; offline, no network).
- DNS + route: `{ "rule_set": ["geosite-category-ads"], "action": "reject" }` — DNS uses `no_drop: true` (avoid sing-box drop-after-50 → Chrome DoH fallback on rapid ad-block tests).
- Apply on next Connect (no live reconnect). UI: Settings → App settings → TUN settings.

---

## Route и TUN

- TUN: **Wintun** (Windows), `strict_route: true` (blocks Windows multihomed DNS leak → adblock), `172.19.0.1/30`
- Split `0.0.0.0/1` + `128.0.0.0/1` — всегда в `route_address`
- `route_exclude_address` **всегда**: `127.0.0.0/8`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local (`169.254/16`) + uplink IPs
- Route: **process bypass** → `127.0.0.0/8` / private direct → **geoip** rule-sets → uplink IPs → sniff (`timeout: 300ms`) → hijack-dns → QUIC reject (`method: default`) → **ads reject** (if on; after sniff for SNI) → **geosite** / site hosts / bootstrap → IPv6 reject (`method: default`) → proxy
- Windows process bypass = route + DNS `process_name` (не OS `exclude_package`); список из API-групп + свои `.exe` + всегда `sing-box.exe` / app; смена TUN → reconnect; IP/process **до** sniff (TCP pre-match останавливается на sniff)
- `default_domain_resolver`: `dns-direct`, `ipv4_only`

(Android использует gVisor TUN + `override_android_vpn`; на Windows — Wintun adapter.)

### API geo-bypass (из `clientConfigJson`)

Servermaster кладёт remote `route.rule_set` (RU/IR/CN) в `clientConfigJson`. Клиент парсит tags/URLs в `ApiBypassRuleSet` и собирает Windows-конфиг:

- geosite → DNS `dns-direct` + route `direct`
- geoip → route `direct`
- **не** эмитить TUN `route_exclude_address_set` (parity Android)
- `download_detour` → `direct` (bootstrap без chicken-egg через proxy)
- `experimental.cache_file` всегда (`enabled` + `store_dns`); rule-sets тоже пишутся в тот же cache
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

- Post-connect: `TunnelPathProbe.Tcp443ViaDiagAsync("1.1.1.1")` перед `Connected`
- Post-connect health: `VpnController.HealthLoopAsync` (diag TCP каждые 25с)
- Egress: `PublicEgressDiagnostics` (Service Menu)
- Clash API `127.0.0.1:19090` (`experimental.clash_api`) — Home throughput via `GET /traffic`

---

## Auth / Servers

| Endpoint | Назначение |
|----------|------------|
| `POST /auth/mobile/email/send\|confirm` | Email OTP |
| `POST /auth/mobile/totp/verify` | TOTP |
| `POST /auth/mobile/session/exchange\|check` | сессия |
| `POST vpn.example.invalid/vpn/servers` | список hy2 по подписке; UI: SegmentBar + listTitle/`City #N` + DNS reachability |
| `GET account.example.invalid/api/auth/client/me` | Telegram OAuth token |

Headers: `X-Demar-Client: demar-vpn`, `User-Agent: DemarVPN/{ver} (windows)`. Protocol: `demarvpn://auth/callback`.

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
| `Domain/Vpn/*` | Profiles, `SingBoxConnectPlan`, `ApiBypassRuleSet` |
| `Services/Auth/*` | API + session store |
| `Services/Servers/*` | servers fetch/parse |
| `Services/Vpn/SingBoxConfigBuilder.cs` | TUN + DNS + route JSON (`stack: system`) |
| `Services/Vpn/VpnController.cs` | Connect pipeline, UI bridge |
| `Services/Network/UplinkIpv4.cs` | uplink resolve |
| `Services/Vpn/SingBoxProcessEngine.cs` | sing-box.exe subprocess |

Синхронизировать TAGS / pipeline с [demar_vpn_android/readme-core.md](../demar_vpn_android/readme-core.md).
