# HANDOFF — LAN Play + the online-connection investigation

Written for whoever (human or AI) picks this up next. State as of `main` = `5bb399e`.
This file is fork-local, like `.github/workflows/dev-*.yml`: delete both before any upstream PR.

---

## 1. What was done, and where it lives

Six pull requests, all merged into `main`, no open PRs:

| PR | Merge | What |
| --- | --- | --- |
| #1 | `a4d70bb` | Native LAN Play support (feature `d2910f9`, diagnostics `7d6ae5c`, cross-platform `a970d0c`, CI `3d55d4c`) |
| #2 | `ee54fe9` | Multiplayer mode hot-swap at runtime |
| #3 | `32a8eda` | Fix guest online paths that assumed a host socket |
| #4 | `517b160` | LAN Play coexists with online play (`IsGuestActive` gating) |
| #5 | `89014cb` | Explain why a connection to the online service failed (logging only) |
| #6 | `5bb399e` | Report EINPROGRESS for a non-blocking connect on Linux and macOS |

Everything branches from `515040a`, which is also the HEAD of the upstream repo
`NextendoNetwork/Ryujinx-Nextendo` — there are **no newer upstream commits to port**.

**Read `docs/lan-play.md` first.** It has the architecture, the packet-flow diagrams, the runtime
mode-change rules, the platform notes, the diagnostics reference, configuration, testing and known
limitations. This file is the short version plus the things that will bite you.

An offline copy of the whole change set (git bundle + patches + flat diff + every changed file) is
attached to the Notion page "Native Switch LAN Play support in Ryujinx-Nextendo", for when GitHub is
unreachable. Fetch it with `git fetch <bundle> lan-play`.

---

## 2. LAN Play in one screen

New multiplayer mode: Ryujinx *is* a [switch-lan-play](https://github.com/spacemeowx2/switch-lan-play)
client. No second PC, no external `switch-lan-play`, no libpcap, no adapter, no host IP or routing
changes, no admin rights. Settings → Network → Multiplayer mode → LAN Play, relay as `host:port`
(`user:password@host:port` if it asks), Virtual IP empty = automatic. The **Test** button joins the
relay without a game and reports what it sees.

Ryujinx has no emulated Ethernet/ARP/IP layer (guest BSD sockets map 1:1 onto host sockets) and the
relay boundary is a raw IPv4 packet, so this adds the missing packet layer:

```
guest game
 ├─ BSD sockets ─ SocketHelpers.CreateSocket ─ LanPlaySocket (IPollableSocket, host fallback)
 └─ LDN service ─ LanPlayLdnClient ─ LanDiscovery + LanProtocol (ldn_mitm protocol, SHARED)
                                     └─ LanPlayLdnUdpSocket / LdnTcpServer / LdnTcpClient
      both ride → LanPlayNetworkInterface (10.13.x.x, IPv4/UDP/TCP/ICMP, frag + reassembly)
                  → LanPlayClient (SLP framing, 10 s keepalive, SLP frags, auth)
                  → one host UDP socket → relay
```

Code: `src/Ryujinx.HLE/HOS/Services/Ldn/UserServiceCreator/LanPlay/` (+ `Proxy/`).

Relay wire format (from `spacemeowx2/switch-lan-play`, `src/lan-client.c`): `[type][payload]`;
`0x00` keepalive, `0x01` full IPv4 packet, `0x02` ping, `0x03` SLP fragment (16-byte header),
`0x04` auth `sha1(sha1(pw)+challenge)+user`, `0x10` server text. Network `10.13.0.0/16`, broadcast
`10.13.255.255`, and `10.13.37.1` is the switch-lan-play client's own address (never hand it out).

---

## 3. Invariants — breaking these is how bugs get introduced

1. **Relays route by *learned source* address.** Never send from a source you do not intend to claim.
   That is why duplicate-address probes use a throwaway source, and why `Announce()` broadcasts a ping
   on join so the relay learns us before anyone addresses us.
2. Packets whose source is our own address are dropped (relays reflect and flood).
3. **TCP send invariant** `_sndNxt == _sndUna + _sendBuffer.Count`: everything queued is always on the
   wire, no window pacing. FIN correctness depends on it; guard `_sndUna` from passing `_sndNxt`.
4. Out-of-order TCP segments are dropped on purpose and recovered by the peer's retransmission. Do not
   "optimise" throughput without adding a reassembly queue *and* tests.
5. **Push vs pull delivery**: `LanPlayUdpEndpoint.PacketReceived` and `LanPlayTcpConnection.DataReceived`
   bypass the internal queue when a handler is attached. A consumer is event-driven or queue-driven,
   never both. `LanPlayLdnTcpSession`'s constructor drains what is already buffered — keep it, it stops
   the first ldn_mitm `Connect` packet from being lost.
6. **Never cast `ISocketImpl` to `DefaultSocket`.** With LAN Play or RyuLDN it is a virtual socket. This
   was a real crash (PR #3): `SendMMsg`/`RecvMMsg` threw `NullReferenceException` and the SSL service
   threw `InvalidCastException` — i.e. online play died the moment LAN Play was enabled. Use
   `if (Socket is DefaultSocket ds)` plus a generic fallback, and `SocketImplStream` when a `Stream` is
   needed.
7. Any non-host `ISocketImpl` must implement `IPollableSocket`, or `SocketHelpers.Select` throws.
8. `ProxyConfig.ProxyIp` and node `Ipv4Address` are big-endian-valued `uint`s: use
   `NetworkHelpers.ConvertIpv4Address` / `ConvertUint`, never `BitConverter`.
9. **Coexistence contract:** LAN Play must not change guest-visible state unless
   `LanPlayStack.IsGuestActive` is true (set by hosting/joining a local session, or by any traffic on
   `10.13.0.0/16`). nifm keys off it; `ldn::GetIpv4Address` is unconditional because it is only asked
   inside a local session. Scanning must **not** set it — games scan from menus while online.
10. **Stack lifetime:** created lazily, once per session, in `SocketHelpers`; configured by
    `Horizon.ApplyMultiplayerConfiguration()` (at boot and whenever settings change) and disposed in
    `Horizon.Dispose`. `LanPlayLdnClient` does not own it. Re-applying an unchanged configuration must
    stay a no-op, because the settings window re-applies everything on save.
11. **ldn_mitm must stay byte-compatible.** `LanDiscovery`/`LanProtocol` are shared through
    `ILdnNetworkProvider` / `ILdnUdpSocket` / `ILdnTcpSession`; host quirks live in
    `HostLdnNetworkProvider` (second UDP socket bound to the broadcast address on Linux/macOS only).
12. Leave RyuLDN alone; its proxy keeps priority in `SocketHelpers` while registered. Leaving that mode
    now releases it (PR #2) — it never used to, which could hijack guest sockets for the rest of a run.
13. A new setting needs: enum + `ConfigurationFileFormat` (now v74 + a migration) + `SettingsViewModel`
    + `SettingsNetworkView.axaml` + `assets/Locales/Root.json` with **every** language key present, or
    the build validation rewrites the file.
14. On Unix, `SocketException.ErrorCode` is the **native errno**, not the Winsock value. Compare
    `SocketErrorCode` instead (PR #6 fixed one such bug that made a whole workaround Windows-only).
    `WinSockHelper.ConvertError((WsaError)exception.ErrorCode)` is nonetheless correct, because the
    fallback returns the value unchanged and Linux errnos equal `LinuxError` values.

---

## 4. Debugging tools that already exist

- **Test** button (`LanPlayConnectionTest`): virtual address, participants seen, who is hosting.
- Info log: join, chosen address, sessions hosted/joined, LDN network changes with node lists, empty
  scans, relay messages, a traffic summary every 30 s and at shutdown, and the first use of the host
  fallback (so you can see online traffic is not being diverted).
- Warnings: relay silent for 30 s (the most common failure), auth required, address taken, TCP timeout,
  peer reset, socket kind LAN Play cannot carry.
- Settings → Logging → **Enable network logs**: a line per packet plus a reason for every drop
  (`Malformed`, `NotForUs`, `LoopedBack`, `BadHeaderChecksum`, `BadTransportChecksum`, `NoUdpEndpoint`,
  `NoTcpConnection`, `Reassembly`, `RelayFragment`, `UnsupportedProtocol`, `QueueFull`). Trace level
  hex-dumps the first relay datagrams.
- Incoming IPv4/UDP/TCP checksums are verified and failures counted (`LanPlayDiagnostics`).

---

## 5. Build, test, CI

```bash
dotnet build Ryujinx.sln -c Release
dotnet test src/Ryujinx.Tests/Ryujinx.Tests.csproj --filter "FullyQualifiedName~LanPlayTests"
```

22 LAN Play tests, ~6 s, no network, no privileges: `src/Ryujinx.Tests/HLE/LanPlayTests.cs` with an
in-process relay (`TestLanPlayRelay.cs`) reproducing the real relay's routing. **Always add a test
there for a fix** — two "consoles" in one process makes end-to-end reproduction cheap.

CI (fork-local, skipped outside a Nextendo fork):
- `.github/workflows/dev-lan-play-check.yml` — build + LAN Play tests ×3 + a locale-diff check on
  Windows, Linux and macOS; runs automatically on `claude/**`, `lan-play/**`, `dev/**`, `test/**`.
- `.github/workflows/dev-lan-play-builds.yml` — manual; self-contained win-x64 / linux-x64 builds and
  the universal macOS `.app` as artifacts.

---

## 6. LAN Play: what is not validated yet

1. **Real console interop has never been tested.** Ryujinx↔Ryujinx over a relay works. Needed: a console
   running ldn_mitm on the same relay, and a console in a game's own LAN mode. If discovery fails, the
   scan framing is shared code (magic `0x11451400`, UDP 11452 → `10.13.255.255:11452`), so suspect
   addressing or announce timing first, and compare against a capture.
2. TCP throughput under a game that pushes real traffic (see invariants 3–4).
3. SLP fragmentation is implemented but off (`PathMtu = 0`, like the reference client); IPv4
   fragmentation is used instead. Wire `PathMtu` if a relay or path drops fragments.
4. No `10.13.37.1` gateway / "fake internet" (socks5). Internet traffic uses the host stack.
5. Address probing is best effort — a relay cannot answer "who has this address" the way ARP does.

---

## 7. The open problem: NPLN / online connection (`2321-4992`)

**Not caused by LAN Play.** Confirmed by the reporter with LAN Play disabled, and in the LAN Play runs
the log shows `LAN Play cannot carry socket InterNetworkV6, Stream, Tcp; it will use the host networking
stack instead`, i.e. the NPLN socket never touched it. In those same runs the relay transport was
healthy (dozens of packets received from other players, dropped as `NoUdpEndpoint` only because the game
had not entered LAN mode).

**What the log shows.** The guest resolves an NPLN host, opens an `InterNetworkV6` stream socket,
connects to `0.0.0.0:443` (a known quirk of the client losing the address, so the fork substitutes the
DNS redirect noted for that port), then ~1 ms later writes its TLS ClientHello with
`Bsd.SendMMsg TLS hs=0x01 len=213 remote=` — `remote=` empty — which fails `ESHUTDOWN`, then
`Shutdown()` throws `SocketException(107)`, and NPLN reports `InitalizeLibFailed` → `2321-4992`.

**What that proves, measured with a standalone probe (do not re-derive this):**

| case | writable / error set | RemoteEndPoint | send | shutdown |
| --- | --- | --- | --- | --- |
| nothing listening → refused | true / false | null | `Shutdown` | throws 107 |
| accepted, then peer resets (RST) before our write | true / false | null | `Shutdown` | throws 107 |
| accepted, then peer closes gracefully (FIN) | true / false | set | succeeds | ok |

The first two are **indistinguishable** from the guest's side, so the reports mean either "nothing is
listening on the redirect target" or "the front end accepted and immediately reset us". Only a graceful
close is excluded. `SO_ERROR` separates them (`ConnectionRefused` 111 vs `ConnectionReset` 104): PR #5
logs it and PR #6 makes that branch reachable on Linux, so **a build from `5bb399e` or later prints the
reason**. Without rebuilding, a capture also separates them: `SYN → RST` versus
`SYN → SYN-ACK → ACK → RST`.

```bash
sudo tcpdump -ni any 'tcp port 443 and (tcp[tcpflags] & (tcp-syn|tcp-rst)) != 0'
```

**Most likely cause.** The redirect target comes from `NEXTENDO_SERVER_IP`; unset, `DnsMitmResolver`
falls back to `127.0.0.1`, where nothing serves 443 and a refused loopback connect completes in ~0.65 ms
— which matches the timestamps. Self-built and CI test builds are unbaked. Check the **full** log for
`DnsMitmResolver: NEXTENDO_SERVER_IP not set, falling back to loopback` (printed early), and
`env | grep NEXTENDO` in the shell that launched the emulator (GUI launchers do not inherit shell
exports — the fork's own comments note this).

**The certificate angle (raised by the maintainer, and still ahead of us).** In these logs the
ClientHello goes out through `Bsd.SendMMsg`, i.e. the **game's own TLS stack over a plain BSD socket**,
not through the `ssl:` service. So `SslManagedSocketConnection`'s `RemoteCertificateValidationCallback
=> true` and its NPLN ALPN handling (h2 for `npln` hosts and for the session endpoint with SNI
`gs.nintendo.net`) do **not** apply to that connection: once TCP works, the server must present a
certificate the game itself accepts. Historically `2321-4992` was a certificate problem, so expect that
to be the next wall after connectivity. Useful probe:

```bash
openssl s_client -connect <ip>:443 -alpn h2 \
  -servername t-dce9377b-lp1.lp1.t.npln.srv.nintendo.net -brief
```

Also worth grepping a full log for `ServiceSsl`: no such lines means the game never used the `ssl:`
service in that run, so trust was entirely in-game.

**Separate but related:** `save push … HTTP 403` with `linked=True token=True` — the account API is
reachable and rejecting the token. If the game backend authenticates against the same session, that can
block online even with correct IPs.

---

## 8. Server addresses and release builds

Runtime environment variables (no bake needed for testing — bake only changes the *fallback* and stamps
the version):

- `NEXTENDO_SERVER_IP` — every guest lookup of `*.nintendo.net|.com|nintendowifi.net|.co.jp` resolves
  here (account + game backend). Unset → `127.0.0.1`.
- `NEXTENDO_NAT_IP` — the `nncs2-*.n.n.srv.nintendo.net` redirect and NAT responder #2 by default.
- `NEXTENDO_NNCS1_IP` / `NEXTENDO_NNCS2_IP` — the two NAT-check responders, probed host-side on UDP
  10025 and 10125 with test id 101; must be two distinct public IPs; default to the two above.
- `NEXTENDO_API` (default `https://nextendo.network`) and `NEXTENDO_SITE` — account API and website.
- Knobs: `NEXTENDO_GRPC_CONNECT_SYNC=0`, `NEXTENDO_NPLN_DELAY_MS`.

Values come from the backend operator, from `dig +short nextendo.network` for a single-host deployment,
or from an existing official build (`strings -a Ryujinx.HLE.dll | grep -Eo '([0-9]{1,3}\.){3}[0-9]{1,3}'`,
because the bake writes them as literals). GitHub Actions secrets cannot be read back.

Baking, on a throwaway checkout because it rewrites tracked files:

```bash
export NEXTENDO_SERVER_IP=... NEXTENDO_NAT_IP=...
python3 distribution/nextendo/bake_release.py <version> $(git rev-parse HEAD)
dotnet publish src/Ryujinx/Ryujinx.csproj -c Release -r linux-x64 --self-contained true \
  -p:DebugType=embedded -p:Version=<version> -p:SourceRevisionId=$(git rev-parse HEAD) -o publish/linux-x64
```

Verified: the bake still applies cleanly to `main` (`BAKE OK`, three files patched) and the result builds.

**Careful — baking makes a build stricter, not looser.** It flips `ReleaseInformation.IsValid`, which
*enables* the online gates: unstamped dev builds bypass `NextendoBeta.Evaluate()` and the exact
game-version check, while a stamped release must satisfy the backend's `MinAppVersion` and match
`NextendoCompatibleVersion`. Test connectivity with env vars on a dev build first.

---

## 9. Triage cheatsheet

- **"Nobody sees me on LAN Play"** — press Test; then network logs for `out: UDP -> 10.13.255.255` and
  any `in:`. No `in:` at all means relay, port or UDP blocked. Traffic but no scan responses means
  nobody is hosting, or a game/version mismatch.
- **"Peer visible, joining fails"** — that is the TCP 11452 session channel;
  `LanPlayTcpConnection` (look for established / timeout / reset lines).
- **"Works then drops"** — the 30 s silence warning and the `dropped …` counters in the summary.
  `QueueFull` means the guest is not draining, `BadTransportChecksum` means corruption.
- **"Can't reach Nextendo"** — first reproduce with mode Disabled. If it fails there too it is not LAN
  Play; go to section 7.
- **Windows oddities after a relay hiccup** — `SIO_UDP_CONNRESET` is disabled and
  ConnectionReset/NetworkReset/MessageSize/Interrupted are treated as recoverable. Do not "simplify"
  that error handling.

---

## 10. Working style

Match the surrounding Ryujinx style: block namespaces, `_camelCase` fields,
`Logger.X?.Print(LogClass…, …)`. Keep RyuLDN, ldn_mitm and Disabled behaviour intact. Prefer extending
`LanPlayTests.cs` over manual testing, and prefer measuring over reasoning — every claim in section 7
came from a probe, and one earlier claim ("it is definitely refused") was wrong until it was measured
properly. Sources of truth are this tree and https://github.com/spacemeowx2/switch-lan-play.
