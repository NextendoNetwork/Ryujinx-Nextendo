# Native Switch LAN Play support

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

## 11. Testing

```
dotnet test src/Ryujinx.Tests/Ryujinx.Tests.csproj --filter "FullyQualifiedName~LanPlayTests"
```

The tests run an in-process relay that reproduces the routing behaviour of the real one, and cover:
two clients with distinct virtual identities, broadcast distribution, unicast in both directions,
a 3000 byte datagram going through IPv4 fragmentation and reassembly, the TCP handshake with data in
both directions (including an 8000 byte transfer) and an orderly close, duplicate address detection,
server string parsing, teardown and reconnect, guest sockets over the virtual interface with
non-LAN-Play traffic still going out through the host stack, and a full LDN session where one client
creates a network, the other discovers it with a scan and joins it, after which the host reports two
nodes with the right virtual addresses and user names.

For a manual test with two Ryujinx instances, point both at the same relay, start the same game on
both and enter its local multiplayer mode. The interesting log lines are
`LAN Play: connected to relay ...`, `LAN Play: using the virtual address ...` and
`LAN Play: hosting an LDN session on ...`.

## 12. Known limitations and what still needs validation on real hardware

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
