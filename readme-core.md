# Ядро VPN: Sing-box (Hysteria2 + Trojan + VLESS Reality) — Demar VPN Windows

Целевая версия core: **sing-box latest**. Фактическая — в `src/DemarVpn/Assets/singbox/bundled_version.txt` после `scripts/for_builds/singbox-build-windows.py`.

Документация: https://sing-box.sagernet.org/

- [Hysteria2 outbound](https://sing-box.sagernet.org/configuration/outbound/hysteria2/)
- [Trojan outbound](https://sing-box.sagernet.org/configuration/outbound/trojan/)
- [VLESS outbound](https://sing-box.sagernet.org/configuration/outbound/vless/)
- [TLS / Reality](https://sing-box.sagernet.org/configuration/shared/tls/)
- [NaiveProxy outbound](https://sing-box.sagernet.org/configuration/outbound/naive/) (since 1.13; Windows → `libcronet.dll`)
- [DNS rule action](https://sing-box.sagernet.org/configuration/dns/rule_action/) (since 1.11.0)
- [Dial Fields](https://sing-box.sagernet.org/configuration/shared/dial/) — `domain_resolver`

Платформа: **Windows only** (WinUI 3 + sing-box.exe subprocess).

**Итерация:** C# integration — **реализовано** (`VpnController`, `SingBoxConfigBuilder`, `SingBoxProcessEngine`). Auth — `auth.example.invalid`; servers — `vpn.example.invalid` (parity Android).

Connect pipeline: `UplinkIpv4` (attempts=2) → DNS resolve success → config → subprocess (full stop before start) → diag `:10808` → `TunnelPathProbe` (poll CONNECT). Hysteria2 = QUIC/UDP — **без TCP preflight**; Trojan/VLESS — `preferResolvedDial` → `withResolvedUplink`.

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

`resolveUplinkIpv4s` — перед connect на Windows. Список: `POST https://vpn.example.invalid/vpn/servers` → `hysteria2` / `trojan` / `vless_reality` (остальное skip). Расширяемость: новый `IProxyProfile` + `ToConnectPlan` + `ServerProtocol` + parse ветка.

### Добавление протокола

1. `FooProfile : IProxyProfile` — `Protocol`, `PreferResolvedDial`, `ToConnectPlan` → `SingBoxConnectPlan.Foo()`
2. `ServerProtocol` entry (`ApiKey` / `Label`)
3. Parse/preview ветка в `ServersApi` (+ `ServerProfile.FromTag`)
4. Test / API seed (не hardcode в runtime)
5. Official release zip уже включает HY2 + Trojan + VLESS/Reality/uTLS + Naive; `libcronet.dll` рядом с `sing-box.exe`

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
| dial | `PreferResolvedDial = false` (hostname в outbound) |

---

## Trojan

API `protocol`: `trojan`. Outbound type: `trojan`. TCP + TLS + multiplex.

| App field | sing-box key |
|-----------|--------------|
| `server` | `server` |
| `port` | `server_port` |
| `password` | `password` |
| `tlsServerName` | `tls.server_name` |

### Outbound defaults (`SingBoxConnectPlan.Trojan`)

| Поле | Значение |
|------|----------|
| `tls` | enabled + `server_name` |
| `multiplex` | `{ enabled: true }` |
| `domain_resolver` | `dns-direct`, `ipv4_only` |
| dial | `PreferResolvedDial` → `WithResolvedUplink` после resolve |

Bootstrap DNS/route: hostname сервера + TLS SNI (обычно совпадают).

---

## VLESS Reality

Стек: **VLESS + TCP + xtls-rprx-vision + REALITY** (не XHTTP — в stock sing-box 1.14 нет).

API `protocol`: `vless_reality`. Outbound type: `vless`.

| App field | sing-box key |
|-----------|--------------|
| `server` | `server` (VPS domain) |
| `port` | `server_port` |
| `uuid` | `uuid` |
| `flow` | `flow` (default `xtls-rprx-vision`) |
| `tlsServerName` | `tls.server_name` (handshake host, often `www.cloudflare.com`) |
| `publicKey` | `tls.reality.public_key` |
| `shortId` | `tls.reality.short_id` |

### Outbound defaults (`SingBoxConnectPlan.VlessReality`)

| Поле | Значение |
|------|----------|
| `tls.reality` | enabled + public_key + short_id |
| `tls.utls` | enabled + fingerprint `chrome` |
| `domain_resolver` | `dns-direct`, `ipv4_only` |
| `network` / `multiplex` | **не эмитятся** (Vision ∌ mux) |
| dial | `PreferResolvedDial` → `WithResolvedUplink` после resolve |

Uplink host = VPS `server`, не SNI. Bootstrap DNS/route: только uplink hostname (без handshake SNI).

---

## DNS (sing-box 1.11+ rule actions)

| Tag | Тип | Назначение |
|-----|-----|------------|
| `dns-direct` | UDP `1.1.1.1` | bootstrap, domain resolver |
| `dns-remote` | HTTPS `1.1.1.1` | DoH через proxy |

Rules: PTR → reject; `localhost` → predefined; process bypass → `dns-direct`; bootstrap → `dns-direct`; **Ad block** reject (`geosite-category-ads`) **до** site-bypass / geosite (parity Apple: иначе `yandex.ru` пропускает `an.yandex.ru`); site bypass → `dns-direct`; geosite → `dns-direct`. `final: dns-remote`, `ipv4_only`, `reverse_mapping`, `optimistic`. `experimental.cache_file`: `enabled` + `store_dns` (живёт между Connect; без сброса при смене Ad/GEO/bypass).

### Ad block (DNS + route)

- Free: always off. Plus/VIP: default **on** (`tun_settings.json` `AdBlockEnabled` null → on); user can disable.
- Build: `scripts/for_builds/sync_rulesets.ps1` → ads + geosite/geoip RU/IR/CN → `Assets/rule-sets/` (gitignore; called from `build_windows.ps1`).
- Connect: bundled path → sing-box `type: local` / `format: binary` (parity Apple; offline, no runtime HTTP).
- DNS + route: `{ "rule_set": ["geosite-category-ads"], "action": "reject" }` — DNS uses `no_drop: true` (avoid sing-box drop-after-50 → Chrome DoH fallback on rapid ad-block tests).
- Apply on next Connect (no live reconnect). `RuleSets.Prepare` → bundled `.srs`; `cache.db` не сбрасывается при смене тогглов. UI: Settings → App settings → TUN settings.

---

## Route и TUN

- TUN: **Wintun** (Windows), `strict_route: true` (blocks Windows multihomed DNS leak → adblock), `172.19.0.1/30`
- Split `0.0.0.0/1` + `128.0.0.0/1` — всегда в `route_address`
- `route_exclude_address` **всегда**: `127.0.0.0/8`, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), link-local (`169.254/16`) + uplink IPs
- Route: **process bypass** → `127.0.0.0/8` / private direct → **geoip** rule-sets → uplink IPs → sniff (`timeout: 300ms`) → hijack-dns → QUIC reject (`method: default`) → **ads reject** (if on; after sniff for SNI) → **geosite** / site hosts / bootstrap → IPv6 reject (`method: default`) → proxy
- Windows process bypass = route + DNS `process_name` (не OS `exclude_package`); список из API-групп + свои `.exe` + всегда `sing-box.exe` / app; IP/process **до** sniff (TCP pre-match останавливается на sniff)
- Site bypass = DNS + route `domain_suffix` → `dns-direct` / `direct`: API site groups + **custom sites** (Plus/VIP; URL→host, Enter/CSV; `tun_settings.json` `BypassHosts`); Free: custom ignored; apply on next Connect
- `default_domain_resolver`: `dns-direct`, `ipv4_only`

(Android использует gVisor TUN + `override_android_vpn`; на Windows — Wintun adapter.)

### API geo-bypass (из `clientConfigJson`)

Servermaster кладёт `route.rule_set` (RU/IR/CN) в `clientConfigJson`. Клиент парсит tags в `ApiBypassRuleSet` (URL ignored). Connect: `RuleSets.Prepare` → только существующие bundled `.srs`; все `type: local`:

- geosite → DNS `dns-direct` + route `direct`
- geoip → route `direct` (Windows; Apple uses TunBypass CIDR)
- ads → DNS+route `reject` (отдельный тоггл, не в `plan.bypassRuleSets`)
- **не** эмитить TUN `route_exclude_address_set` (parity Android)
- `cache_file.store_dns` (без сброса при смене Ad Block / GEO / bypass)
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
| `POST vpn.example.invalid/vpn/servers` | список HY2/Trojan/VLESS по подписке; UI: SegmentBar + listTitle/`City #N` + DNS reachability |
| `GET account.example.invalid/api/auth/client/me` | Telegram OAuth token |

Headers: `X-Demar-Client: demar-vpn`, `User-Agent: DemarVPN/{ver} (windows)`. Protocol: `demarvpn://auth/callback`.

---

## Сборка ядра (Windows)

```powershell
py -3 scripts/for_builds/singbox-build-windows.py
# перекачать:  py -3 scripts/for_builds/singbox-build-windows.py --force
pwsh scripts/for_builds/build_windows.ps1
```

Runtime в `Native/bin/`: `sing-box.exe`, `wintun.dll`, `libcronet.dll` (NaiveProxy). Official release zip уже включает HY2 + Trojan + VLESS/Reality/uTLS.

Очистка: `python3 scripts/singbox-cleaner.py -y`

---

## C# layout

| Путь | Роль |
|------|------|
| `Domain/Vpn/*` | `IProxyProfile`, HY2/Trojan/VLESS profiles, `SingBoxConnectPlan`, `ApiBypassRuleSet` |
| `Services/Auth/*` | API + session store |
| `Services/Servers/*` | servers fetch/parse |
| `Services/Vpn/SingBoxConfigBuilder.cs` | TUN + DNS + route JSON (`stack: system`; local `.srs`) |
| `Services/Vpn/RuleSets.cs` | Bundled `.srs` paths, `Prepare` |
| `Services/Vpn/VpnController.cs` | Connect pipeline, UI bridge |
| `Services/Network/UplinkIpv4.cs` | uplink resolve |
| `Services/Vpn/SingBoxProcessEngine.cs` | sing-box.exe subprocess |

Синхронизировать TAGS / pipeline с [demar_vpn_android/readme-core.md](../demar_vpn_android/readme-core.md).
