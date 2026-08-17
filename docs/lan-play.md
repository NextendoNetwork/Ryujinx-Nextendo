# Native Switch LAN Play support

> Picking this up cold? Start with [`HANDOFF.md`](../HANDOFF.md) in the repository root: it summarises
> the state of the work, the invariants to respect and the open online-connection investigation.

Ryujinx can join a [switch-lan-play](https://github.com/spacemeowx2/switch-lan-play) relay by itself.
No second PC, no external `switch-lan-play` process, no libpcap, no virtual adapter, no host routing
changes and no manual 10.13.x.x configuration on the host are needed.

Select **LAN Play** under *Settings → Network → Multiplayer mode*, enter the relay
(`host:port`, for example `switch.example.com:11451`), and launch a game that supports local
multiplayer. Every player, real console or emulator, that uses the same relay can see each other.

---

## 1. How Ryujinx networking is put together

| Layer | Where |
| --- | --- |
| Guest BSD sockets | `HOS/Services/Sockets/Bsd` → `ManagedSocket` → `SocketHelpers.CreateSocket` |
| Host sockets | `Bsd/Proxy/DefaultSocket.cs` (a plain `System.Net.Sockets.Socket`) |
| RyuLDN socket proxy | `Ldn/UserServiceCreator/LdnRyu/Proxy/LdnProxySocket.cs` |
| LDN service | `Ldn/UserServiceCreator/IUserLocalCommunicationService.cs` picks an `INetworkClient` per `MultiplayerMode` |
| ldn_mitm protocol | `Ldn/UserServiceCreator/LdnMitm/{LanDiscovery,LanProtocol}.cs` |

Findings that drove the design:

* **Ryujinx has no emulated Ethernet, ARP, IPv4 or UDP layer.** Guest socket calls are translated
  one to one into host socket calls, and the host OS stack does all the packet work. There was no
  packet layer to plug a LAN Play client into, so this feature had to add one.
* **RyuLDN is a layer 4 proxy, not a packet bridge.** `LdnProxy` wraps payloads in RyuLDN
  `ProxyData` messages carrying source/destination virtual IP, port and protocol, and the LDN server
  routes them. Useful as an architectural reference, but the LAN Play relay expects IPv4 packets, so
  LAN Play could not simply be another `LdnProxySocket`.
* **ldn_mitm mode already produces console-compatible LAN traffic**: UDP broadcast and unicast on
  port 11452 for scan requests and responses, TCP on port 11452 for the session channel, and the
  game's own data sockets on top. It sends all of that through *host* sockets bound to the host NIC,
  which is exactly why the manual experiment (host NIC set to 10.13.x.x plus an external
  switch-lan-play) worked.

## 2. How switch-lan-play actually works

Read from the reference client (`src/lan-client.c`, `src/packet.c`, `src/ipv4/ipv4.c`, `src/config.h`):

* The client captures Ethernet frames with libpcap, answers ARP locally, and for IPv4 frames whose
  destination is inside `10.13.0.0/255.255.0.0` it forwards **the complete raw IPv4 packet, starting
  at the IP header and with no Ethernet header**, to the relay. Broadcasts are both forwarded and
  re-injected on the local network.
* Relay framing is one UDP datagram per packet: `[1 byte type][payload]`, where the top bit of the
  type byte marks encryption (unused by known relays).

  | Type | Meaning |
  | --- | --- |
  | `0x00` KEEPALIVE | empty, sent every 10 seconds |
  | `0x01` IPV4 | a complete IPv4 packet |
  | `0x02` PING | ignored by the client |
  | `0x03` IPV4_FRAG | 16 byte header (`src[4] dst[4] id:u16 part:u8 total:u8 len:u16 pmtu:u16`, big endian) followed by a chunk |
  | `0x04` AUTH_ME | challenge; the answer is `sha1(sha1(password) + challenge)` followed by the user name |
  | `0x10` INFO | text from the relay |

* The relay is game agnostic. It routes on the destination address inside the IPv4 packet and floods
  the subnet broadcast address `10.13.255.255` to every client. `10.13.37.1` is the address the
  client itself answers on (its gateway / "fake internet" feature), so it is never used by a console.

## 3. Integration point

LAN Play is inserted as a **virtual Switch network interface with a small user space IPv4 stack**,
transported by an embedded switch-lan-play client. It is exposed through the two abstractions
Ryujinx already has, so nothing above it had to learn about LAN Play:

```
   Switch game
        |
   BSD sockets (ManagedSocket)          LDN service (IUserLocalCommunicationService)
        |                                        |
 SocketHelpers.CreateSocket               LanPlayLdnClient
        |                                        |
   LanPlaySocket                          LanDiscovery + LanProtocol   (ldn_mitm protocol, unchanged)
        |                                        |
        |                            LanPlayLdnUdpSocket / LanPlayLdnTcpServer / LanPlayLdnTcpClient
        |                                        |
        +----------------+-----------------------+
                         |
             LanPlayNetworkInterface       10.13.x.x, IPv4 + UDP + TCP + ICMP,
                         |                 fragmentation and reassembly
                  LanPlayClient            SLP framing, keepalive, SLP fragments, auth
                         |
                    host UDP socket
                         |
                  LAN Play relay
```

Why this layer and not the BSD socket layer alone: the relay boundary is IPv4, so the packets have
to be built somewhere, and a virtual interface serves both traffic kinds a console produces on a LAN
Play network — LDN sessions translated by the ldn_mitm protocol *and* the plain IP traffic of games
that implement their own LAN mode. A `LanPlaySocket`-only design would have covered the second case
badly and would have duplicated IP handling.

Why not the ldn_mitm host sockets: they need a host interface owning a 10.13.x.x address, which is
precisely the manual setup this feature removes.

## 4. Files

New, in `src/Ryujinx.HLE/HOS/Services/Ldn/UserServiceCreator/LanPlay`:

| File | Role |
| --- | --- |
| `LanPlayProtocol.cs` | relay wire format, network constants, auth response |
| `LanPlayClient.cs` | the embedded switch-lan-play client: relay socket, keepalive, SLP fragmentation and reassembly, auth, server messages |
| `LanPlayConfiguration.cs` | parses `[user[:password]@]host[:port]` and the virtual address setting |
| `Ipv4Packet.cs` | IPv4 header reading and writing, internet and transport checksums |
| `LanPlayNetworkInterface.cs` | the virtual NIC: addressing, IPv4 in and out, fragmentation, reassembly, demultiplexing, ICMP echo |
| `LanPlayUdpEndpoint.cs` | a bound UDP port with a receive queue and a push mode |
| `LanPlayTcpConnection.cs` | user space TCP: handshake, cumulative acknowledgements, retransmission, orderly and abortive close |
| `LanPlayTcpListener.cs` | listening TCP port and accept queue |
| `VirtualAddressAllocator.cs` | picks and probes the 10.13.x.x address |
| `LanPlayStack.cs` | the client plus the interface for one emulation session |
| `LanPlayLdnClient.cs` | `INetworkClient` for the LAN Play multiplayer mode |
| `Proxy/LanPlaySocket.cs` | `ISocketImpl` for the emulated console's sockets |
| `Proxy/LanPlayLdn*.cs` | ldn_mitm discovery sockets carried by the virtual interface |
| `LanPlayDiagnostics.cs` | counters, per packet tracing, periodic summary, relay silence warning |
| `LanPlayConnectionTest.cs` | the *Test* button: joins the relay and reports what answered |

Refactored so that the ldn_mitm protocol can run over either transport (behaviour for ldn_mitm mode
is unchanged):

* `LdnMitm/Proxy/ILdnNetworkProvider.cs`, `ILdnUdpSocket.cs`, `ILdnTcpSession.cs`,
  `LdnScanResults.cs`, `HostLdnNetworkProvider.cs` — new abstractions and the host implementation.
* `LdnMitm/LanDiscovery.cs`, `LanProtocol.cs`, `LdnProxyUdpServer.cs`, `LdnProxyTcpServer.cs`,
  `LdnProxyTcpClient.cs`, `LdnProxyTcpSession.cs`, `LdnMitmClient.cs` — use the abstractions.

Other changes:

* `Bsd/Proxy/SocketHelpers.cs` — LAN Play registration (lazily connected on first use) and
  `IPollableSocket` handling in `Select`; `Bsd/Proxy/IPollableSocket.cs` is new.
* `HOS/Horizon.cs` — enables LAN Play for the session and tears it down.
* `Nifm/StaticService/IGeneralService.cs` — reports the virtual address as the console's IP while
  LAN Play is active, for games that implement their own LAN mode.
* `MultiplayerMode.cs`, `HleConfiguration.cs`, `ConfigurationState*`, `ConfigurationFileFormat.cs`
  (version 74), `SettingsViewModel.cs`, `SettingsNetworkView.axaml`, `assets/Locales/Root.json`.
* `src/Ryujinx.Tests/HLE/LanPlayTests.cs`, `TestLanPlayRelay.cs` — tests with an in-process relay.

## 5. Packet flow

Ryujinx → relay:

```
game sendto(10.13.4.7:11452)  ->  LanPlaySocket  ->  LanPlayUdpEndpoint
  -> LanPlayNetworkInterface: UDP header + checksum, IPv4 header (src 10.13.x.x, dst 10.13.4.7),
     fragmented at 1500 bytes if needed
  -> LanPlayClient: [0x01][IPv4 packet]  (or [0x03][frag header][chunk] when a path MTU is set)
  -> host UDP socket -> relay -> the client that owns 10.13.4.7
```

relay → Ryujinx:

```
relay -> host UDP socket -> LanPlayClient: type check, SLP fragment reassembly
  -> LanPlayNetworkInterface: parse IPv4, drop packets not addressed to us or looped back,
     reassemble IPv4 fragments
  -> UDP port table / TCP connection table / ICMP
  -> LanPlayUdpEndpoint queue or LanPlayTcpConnection buffer
  -> LanPlaySocket.ReceiveFrom  ->  game recvfrom
```

## 6. Virtual addressing

The host keeps its own address (for example 192.168.1.50); the emulated console gets a 10.13.x.x
address that only exists inside Ryujinx. When the *Virtual IP* setting is empty, an address is
picked at random inside 10.13.0.0/16, skipping `.0`, `.255`, `10.13.0.1` and the switch-lan-play
address `10.13.37.1`, and probed first: an ICMP echo request is sent to the candidate from a
throwaway address, and an echo reply means the address is taken. Up to four candidates are tried.
The probe uses a throwaway source because a relay routes to whichever client last used an address,
so probing "as" the candidate would only reach ourselves.

The address is reported to the guest through `ldn::GetIpv4Address` (as `ProxyConfig.ProxyIp`, the
same path RyuLDN uses) and through `nifm::GetCurrentIpAddress`. Right after joining, the interface
announces itself with a broadcast ICMP echo request, so the relay knows which client owns the
address before anybody tries to reach it.

## 7. Broadcast and discovery

`10.13.255.255` (and `255.255.255.255`, which a guest may use) is flooded by the relay to every
client, which is what LDN scanning relies on: `LanProtocol.SendBroadcast` sends the ldn_mitm scan
request from UDP port 11452 to the broadcast address, hosts answer with a unicast scan response, and
the session channel is a TCP connection to the host's virtual address on port 11452. Incoming
broadcasts are delivered to every matching UDP endpoint and flagged, so a guest socket can tell a
broadcast from a unicast. Packets that appear to come from our own address are dropped, so our own
broadcasts do not come back to us.

## 8. Relationship with RyuLDN and ldn_mitm

The modes stay mutually exclusive and independent:

* **RyuLDN** keeps using `LdnMasterProxyClient` and `LdnProxy`. `SocketHelpers` still gives the LDN
  proxy priority, so nothing changes when it is registered.
* **ldn_mitm** keeps using host sockets on the selected network interface, through
  `HostLdnNetworkProvider`, which contains the same code (including the Linux/macOS double bind)
  that `LanDiscovery` had inline before.
* **LAN Play** shares the ldn_mitm protocol implementation (`LanDiscovery`, `LanProtocol`) so that
  it is byte compatible with consoles running ldn_mitm, but carries it over the virtual interface.
* **Disabled** is untouched, and LAN Play is only activated when the mode is explicitly selected.

## 9. Building

```
dotnet build src/Ryujinx/Ryujinx.csproj -c Release
```

## 10. Configuration

*Settings → Network → Multiplayer mode → LAN Play*, then:

* **LAN Play Server** — `host:port`, for example `switch.example.com:11451`. The port defaults to
  11451 when omitted. If the relay requires a login, use `user:password@host:port`.
* **Virtual IP** — leave empty for an automatic address, or force one inside `10.13.0.0/16`.

Stored in `Config.json` as `multiplayer_lan_play_server` / `multiplayer_lan_play_virtual_ip`
(configuration version 74; older configuration files are migrated automatically).

## 11. Changing the multiplayer mode while a game is running

The multiplayer mode can be changed without restarting the emulator or the game. `Horizon` applies
the multiplayer configuration at boot and the front end re-applies it whenever the settings change
(`AppHost.UpdateMultiplayerModeState` and the LAN Play server / virtual IP handlers), which reaches
`SocketHelpers.ApplyMultiplayerMode`.

What happens immediately, at the moment the mode changes:

| Change | Effect |
| --- | --- |
| anything to **LAN Play** | The relay is joined in the background, new guest sockets use the virtual interface, and the console reports its 10.13.x.x address. |
| **LAN Play** to anything else | The relay is left and the stack is disposed. New guest sockets go back to the host stack, and the console reports its host address again. |
| **RyuLDN** to anything else | The RyuLDN socket proxy is released, so guest sockets stop being routed into the LDN network. |
| LAN Play relay or virtual IP edited | The relay is rejoined with the new settings. Re-saving the same values changes nothing, because the settings window re-applies everything on save. |

What deliberately does not change under the running game:

* **Sockets that are already open keep their transport.** A socket the game opened while LAN Play was
  active keeps its host fallback, so traffic to anything outside the LAN Play network keeps flowing
  after the switch; its virtual side simply goes quiet. This is what keeps an online session alive
  when the mode is switched off mid-game, and there is a test for it.
* **An LDN session does not migrate.** The LDN client is chosen when the game initialises LDN, so a
  session that is already running stays on the transport it started with; the guest gets errors and
  empty scan results instead of exceptions if the transport is taken away under it. Leaving and
  re-entering the game's local multiplayer menu makes the game re-initialise LDN, at which point the
  new mode is used.

### Interaction with online play

LAN Play and the online stack are designed to coexist, and LAN Play stays out of the way until it is
actually used. Concretely:

* **Traffic.** Only what is addressed to the LAN Play network goes to the relay: 10.13.0.0/16,
  the broadcast addresses, and the broadcast address of one of the host's own networks (192.168.0.255
  for a host on 192.168.0.91/24). That last case is a game's own LAN mode broadcasting before the
  console started presenting its LAN Play address, so it is LAN discovery and is re-addressed to
  10.13.255.255 on the way out; the relay only floods that address, and a peer drops anything else as
  addressed to somebody else. Everything else — the online service, the DNS MITM, the NAT check —
  keeps using the host network stack through the socket's host fallback, and a line is logged the
  first time that fallback is used so it is visible in a log. Options the game set on the socket
  before that fallback existed are replayed on it, SO_BROADCAST included, without which a broadcast
  fails with EACCES on Linux and macOS.
* **The console's address.** `ldn::GetIpv4Address` always answers with the LAN Play address, because
  it is only asked in the context of a local session. `nifm::GetCurrentIpAddress` and
  `GetCurrentIpConfigInfo` only answer with it once the game is actually using the LAN Play network
  (`LanPlayStack.IsGuestActive`): hosting or joining a local session, or exchanging any traffic on the
  LAN Play network, which is what entering a game's own LAN mode does — for example holding ZR + ZL +
  L3 in a Splatoon private battle. A game that entered its LAN mode broadcasts on the network it was
  told it is on, so the first such broadcast is what marks the LAN Play network as in use; the game
  has to re-enter its LAN mode for the LAN Play address to be the one it advertises to its peers. Before that, and for a session that only ever plays online, the
  host address is reported exactly as it would be with LAN Play switched off. Merely selecting LAN
  Play therefore changes nothing for online play.
* **Nothing else is conditioned on the mode.** `GetCurrentNetworkProfile`, the DNS redirects, the NAT
  check and the account services are untouched, and LAN Play binds virtual ports only, so it cannot
  collide with a port the online stack uses.

Two guest paths used to break this coexistence and are fixed (see below): vectored I/O and the SSL
service used to require a host socket.

If an online feature still misbehaves with LAN Play selected, switching the mode to Disabled takes
effect immediately: no restart, and any socket the game already had open keeps working. A game that
cached its address earlier may need its online menu re-entered before it asks for it again.

## 12. Platform support

Windows, Linux and macOS behave the same, because everything happens in managed code on top of one
ordinary UDP socket:

* **No libpcap, no adapter, no privileges.** Nothing is captured or injected on a host interface, so
  none of the platform specific capture stacks (Npcap, `AF_PACKET`, BPF) is involved, and the
  emulator does not need administrator or root rights on any platform. Even a *virtual* port below
  1024 works everywhere, because that port only exists inside Ryujinx.
* **No routing, firewall or interface changes.** The host keeps its own address; only outbound UDP to
  the relay is used, which is what a normal firewall already allows for the emulator.
* **Byte order is explicit.** All packet fields are read and written with `BinaryPrimitives` in
  network order, so the code does not depend on the host being little endian (relevant for macOS on
  Apple silicon as much as for anything else).
* **Teardown does not rely on platform behaviour.** Closing a socket does not reliably abort a
  blocking receive on Unix, so the relay receive loop wakes up twice a second and checks its own stop
  flag; `Dispose` then joins the thread and warns if it is stuck. A test asserts a session tears down
  in well under two seconds.
* **The one platform specific line** is `SIO_UDP_CONNRESET`, disabled on Windows only: without it a
  Windows UDP socket fails its *next* receive with `WSAECONNRESET` after an ICMP "port unreachable",
  which happens whenever the relay is briefly down. Linux and macOS do not do this, and neither does
  a real console. The receive loop also treats that error, `WSAENETRESET`, truncated datagrams and
  `EINTR` as recoverable, so one bad moment cannot end a session. A test covers a relay that is down
  and then comes back.
* **ldn_mitm mode keeps its platform quirks** exactly as before: `HostLdnNetworkProvider` still binds
  the second UDP socket to the broadcast address on Linux and macOS, which those platforms need in
  order to receive broadcasts, and not on Windows, which does not accept it.

The one difference a user may notice is unrelated to LAN Play itself: on any platform where the
emulator is behind a NAT that rewrites source ports very aggressively, the relay may need a few
keepalives before it routes to the right client, which is also true of the reference client.

## 13. Debugging a session

Everything below is available in a normal build; no debugger is needed.

**Test button.** *Settings → Network*, next to the LAN Play server field. It joins the relay without
starting a game, picks a virtual address, sends the same LDN scan request a console sends, listens
for four seconds and reports the virtual address it would use, the other participants it saw, and
whether any of them is hosting a local session. This separates "my relay setting is wrong or my
connection is blocked" from "the game is not doing what I expect".

**Normal log (info).** Joining, the chosen virtual address, LDN sessions being hosted or joined,
every LDN network change with the node list, scan results, messages sent by the relay, and a traffic
summary every 30 seconds plus a final one when the session ends:

```
LAN Play: connected to relay switch.example.com:11451. Enable "Network logs" ...
LAN Play: using the virtual address 10.13.42.7.
LAN Play: hosting an LDN session on 10.13.42.7:11452.
LAN Play LDN: network update, 2 node(s): #0 10.13.42.7, #1 10.13.8.31
LAN Play status: relay switch.example.com:11451, sent 214 packets (33012 bytes, 4 keepalives),
  received 198 packets (30122 bytes), udp 210/196, tcp 3/1, icmp 1/1, dropped NotForUs=87
```

**Warnings that point at the usual causes.** The relay going quiet is reported once, with the likely
cause, and again when it comes back:

```
LAN Play: nothing received from the relay ... in 30 seconds. Check the address and port,
  that outbound UDP is not blocked, and that the relay is up.
LAN Play: the relay ... is answering again.
```

Other warnings cover a relay that asks for credentials, a virtual address that is already taken, a
socket kind LAN Play cannot carry (which then uses the host stack), a TCP connection timing out after
its retransmissions, and a peer resetting a connection.

**Network logs (per packet).** Enable *Settings → Logging → Enable network logs* to get a line per
packet in each direction plus a line for every dropped packet with the reason:

```
LAN Play out: UDP -> 10.13.255.255 68 bytes
LAN Play in: UDP 10.13.8.31 -> 10.13.42.7 ports 11452 -> 11452 1124 bytes
LAN Play: dropped a packet (BadTransportChecksum): udp 11452 -> 11452
```

Drop reasons: `Malformed`, `NotForUs`, `LoopedBack`, `BadHeaderChecksum`, `BadTransportChecksum`,
`NoUdpEndpoint`, `NoTcpConnection`, `Reassembly`, `RelayFragment`, `UnsupportedProtocol`,
`QueueFull`. `NotForUs` and `LoopedBack` are normal on a relay that floods, and are counted but not
logged per packet.

**Trace logs.** With trace logging enabled, the first eight non-keepalive relay datagrams are dumped
as hex, which is what to attach when the framing itself is suspect.

Incoming packets are validated the way a real stack does: the IPv4 header checksum, and the UDP (when
present) and TCP checksums are verified, and failures are counted rather than silently ignored, so a
relay or peer sending malformed traffic shows up immediately in the summary.

## 14. Testing

```
dotnet test src/Ryujinx.Tests/Ryujinx.Tests.csproj --filter "FullyQualifiedName~LanPlayTests"
```

The tests run an in-process relay that reproduces the routing behaviour of the real one, and cover:
two clients with distinct virtual identities, broadcast distribution, unicast in both directions,
a 3000 byte datagram going through IPv4 fragmentation and reassembly, the TCP handshake with data in
both directions (including an 8000 byte transfer) and an orderly close, duplicate address detection,
server string parsing, teardown and reconnect, guest sockets over the virtual interface (including
`SocketHelpers.Select`) with non-LAN-Play traffic still going out through the host stack, a full LDN
session where one client creates a network, the other discovers it with a scan and joins it, after
which the host reports two nodes with the right virtual addresses and user names, the connection test
finding that hosted session, a session tearing down promptly and surviving a relay that is down and
comes back, switching the multiplayer mode at runtime in both directions (including that a socket
opened while LAN Play was active keeps reaching the host network afterwards, that re-saving unchanged
settings does not rejoin, that editing them does, that dropping LAN Play under a hosted LDN session
degrades gracefully, and that leaving RyuLDN releases its socket proxy), and packets whose checksums
are wrong (built and checksummed by the test
itself, independently of the stack) being accepted or dropped and counted as expected.

For a manual test with two Ryujinx instances, point both at the same relay, start the same game on
both and enter its local multiplayer mode. The interesting log lines are
`LAN Play: connected to relay ...`, `LAN Play: using the virtual address ...` and
`LAN Play: hosting an LDN session on ...`.

## 15. Continuous integration for this branch

Two workflows were added for testing this feature, both prefixed `dev-` and both kept out of the
upstream project: they never run on `main`, on a tag or on a release, and every job is skipped unless
the workflow runs in a Nextendo fork. Deleting `.github/workflows/dev-*.yml` removes them entirely,
which is what to do before upstreaming.

**`dev-lan-play-check.yml` — cross-platform check.** Runs on a push to `claude/**`, `lan-play/**`,
`dev/**` or `test/**`, or by hand from the Actions tab. On Windows, Linux and macOS (arm64) it builds
the whole solution, checks the build did not rewrite `assets/Locales` (which is how a missing
translation key shows up), runs the LAN Play tests three times because they are timing sensitive and
the hosted runners are slow, then runs the remaining HLE tests for information only. Test results are
uploaded as `.trx` artifacts per platform. `fail-fast` is off, so one platform failing still reports
the other two.

**`dev-lan-play-builds.yml` — runnable test builds.** Manual runs only, with a choice of
configuration and of which platforms to build. It produces self-contained builds for `win-x64` and
`linux-x64`, and the universal `.app` for macOS through the maintained
`distribution/macos/create_macos_pr_build_ava.sh`, each as a downloadable artifact, and writes
instructions for trying LAN Play across machines into the run summary. These are test builds: the
Nextendo server addresses are not baked in, so anything other than LAN Play falls back to loopback.
Use the release workflow for real builds.

## 16. Known limitations and what still needs validation on real hardware

* **Not yet tested against a real console.** Everything above is validated between Ryujinx instances
  and against a relay implementation derived from the reference client's behaviour. The interop that
  still needs confirmation on hardware is a real Switch running ldn_mitm on the same relay, and a
  real Switch using a game's own LAN mode.
* **TCP is deliberately minimal.** In order delivery with cumulative acknowledgements, a single
  retransmission timer and no window based pacing: out of order segments are dropped and recovered
  by retransmission. That matches the small, low latency exchanges of LDN sessions, but a game that
  pushes a lot of TCP data over LAN Play may see lower throughput than on hardware.
* **SLP fragmentation is implemented but off by default** (`PathMtu` is 0), like the reference
  client without `--pmtu`. Oversized packets are fragmented at the IPv4 level instead. Incoming SLP
  fragments are always reassembled.
* **No gateway or "fake internet".** `10.13.37.1` is not emulated, and the relay's socks5 based
  internet feature is not implemented. Regular internet traffic keeps using the host stack.
* **Address probing is best effort**, because a relay cannot answer "who has this address" the way
  ARP does on a real LAN. With a /16 the chance of a collision is negligible, and an address can be
  forced in the settings.
* **No IPv6 on the virtual network**, matching switch-lan-play.
* **Relay authentication** is implemented from the reference client's auth type 0 only; other auth
  types are logged and ignored.
