# Classics client integration handoff

This change ports the opt-in GBA, NES and Game Boy friend-resolution experiment
from the local 1.8.11 campaign onto current upstream. It preserves current Wonder,
Splatoon 3, pagination, account authentication and ARMS behavior. The experiment
waits at most 3000 ms using the existing `NextendoFriends.GetWarm` when
`UpdateFriendInfo` resolves the discovered room host. IDs remain in requested order,
unresolved entries remain invalid, and only counts are added to diagnostics.

| Application | Version tested historically | Opt-in variables |
| --- | --- | --- |
| GBA `010012F017576000` | 3.3.0 | `NEXTENDO_GBA_FRIEND_TRACE=1` and `NEXTENDO_GBA_FRIEND_WARM=1` |
| NES `0100D870045B6000` | 9.1.0 | `NEXTENDO_NES_FRIEND_TRACE=1` |
| Game Boy `0100C62011050000` | 4.1.0 | `NEXTENDO_GB_FRIEND_TRACE=1` |

The GBA cold-cache trace returned zero details for a discovered host. A subsequent
run resolved the requested friend, and the operator reported gameplay and host
reversal. Routing changed in that campaign too: the wait is not proven as the sole
cause. NES/Game Boy operator reports likewise describe historical lab builds.
This port needs acceptance on the current client and signed-account backend.
An unavailable or genuinely empty friend list can consume the full wait. The
existing nonblocking path remains the default; no fabricated identities are used.

## Windows secondary-process callback lifetime

The separate callback repair keeps each Windows unwind callback delegate in its
own retained function-table handler. Previously one static delegate was overwritten
when another JIT cache installed a table; the first native table could still call a
collected delegate. The handler dictionary retains each delegate until successful
table removal. This repair is independent of server deployment and friend caching.

The local N64 investigation observed that lifetime defect. Retaining delegates did
not fix invalid guest reads, shader failures or Vulkan device loss. The experimental
firmware TLS handle write and private CPU/GPU capture tools are not included here.
N64 4.2.0 stable gameplay remains unverified in Ryujinx; upstream already includes
the DMA range guards associated with commit `9a4445d`.

## Maintainer acceptance

Build with the repository's .NET toolchain. On Windows, exercise two simultaneous
JIT processes, force managed collection while both tables are installed, and check
unwind/disposal across both orders. Repeat cold/warm/empty/unavailable friend-cache
conditions, requested-ID order, and unrelated NEX/NPLN titles with the opt-in unset.
For each Classics title, record exact client/server revisions, title version, build
ID, host roles, discovery, gameplay, suspend/resume, rejoin and five-minute AFK.
Then repeat Switch pairings, separate networks and four players where applicable.

Backend work is tracked in
[online-classics PR 5](https://github.com/NextendoNetwork/online-classics/pull/5).
No firmware, games, IPS patches, account credentials, network captures or binaries
are included. Client changes alone do not deploy or certify the server.
