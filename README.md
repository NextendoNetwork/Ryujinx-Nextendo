<h1 align="center">Nextendo</h1>

<p align="center">
  <b>A Nintendo Switch emulator with built-in support for the Nextendo Network private online service.</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/based%20on-Ryujinx-blue" alt="Based on Ryujinx">
  <img src="https://img.shields.io/badge/license-PolyForm%20Shield%201.0.0-orange" alt="License: PolyForm Shield 1.0.0">
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey" alt="Platforms">
</p>

---

## What is Nextendo?

**Nextendo** is a fork of the open-source [Ryujinx](https://github.com/Ryubing/Ryujinx) Nintendo Switch
emulator. On top of Ryujinx's excellent accuracy and performance, Nextendo adds a first-class,
built-in client for the **Nextendo Network** — a community-run replacement for the official online
service — so supported games can be played online again without any manual configuration, custom
hosts file, or SSL bypass on the user's part.

The emulation core (CPU, GPU, audio, input, filesystem) is unchanged from upstream Ryujinx. Nextendo's
additions are limited to the networking layer and the surrounding user experience.

> Nextendo is an independent, non-commercial project. It is not affiliated with, endorsed by, or
> associated with Nintendo. "Nintendo Switch" and all game titles are trademarks of their respective
> owners. This project ships **no** Nintendo code, keys, or copyrighted assets — you must provide your
> own legally dumped games and system files, exactly as with upstream Ryujinx.

## Online support

Online play is currently implemented for the following titles. Each connects to the Nextendo Network
the same way it would connect to the official service — the emulator transparently points the game's
online hostnames at the configured Nextendo servers.

| Game                          | Status   | Notes                                              |
| ----------------------------- | -------- | -------------------------------------------------- |
| Mario Kart 8 Deluxe           | Playable | Worldwide races, in-game lobbies.                  |
| Splatoon 2                    | Playable | Turf War, private battles, Salmon Run, Splatfests. |
| Super Smash Bros. Ultimate    | Playable | Online arenas.                                     |

More titles are being worked on.

## How online works

Nextendo implements the client side of a private online network entirely inside the emulator, so no
external tools are needed:

- **Hostname redirection.** A built-in DNS-MITM resolver redirects the game's online hostnames to the
  configured Nextendo servers. Exact hosts-file entries still take precedence, so a custom server can
  be targeted without rebuilding.
- **Account tokens.** The emulator locally signs the account (BAAS) `id_token` that some titles verify
  before allowing online entry. The signing key is **not** bundled — it is supplied at runtime (see
  [Configuration](#configuration)).
- **NAT & P2P.** NEX titles use peer-to-peer networking (Pia) once matchmaking completes. Nextendo
  keeps the emulator's UDP NAT mapping alive across the P2P bring-up so hole-punching succeeds.
- **First-run wizard, online status, in-game friends, and a kill-switch** round out the experience.

None of the server infrastructure is hardcoded in this repository — addresses and keys come from
environment variables and fall back to loopback when unset, so an unconfigured build simply behaves
like stock Ryujinx offline.

## Configuration

Point a build at a Nextendo Network server (or your own) with these environment variables:

| Variable                     | Purpose                                                            | Fallback   |
| ---------------------------- | ----------------------------------------------------------------- | ---------- |
| `NEXTENDO_SERVER_IP`         | Address the main online hostnames resolve to.                     | `127.0.0.1`|
| `NEXTENDO_NAT_IP`            | Address of the second NAT-check responder (required by NAT probe).| `127.0.0.1`|
| `NEXTENDO_BAAS_SIGNING_KEY`  | PEM of the RSA key used to sign account `id_token`s. May instead be placed in a `nextendo_baas.pem` file next to the executable. | none (throwaway key) |

If none are set, online features stay dormant and Nextendo runs as a normal offline emulator.

## System requirements

To run comfortably, your PC should have at least:

- 8 GiB of RAM
- 6 CPU cores
- A GPU released within the last 10 years, supporting OpenGL 4.6 or Vulkan 1.4
- Windows 10 (20H1) or newer, a modern Linux distribution, or macOS Big Sur (Apple Silicon) or newer

Failing to meet these requirements may result in poor performance or crashes.

## Building

Nextendo builds like upstream Ryujinx — you need the .NET SDK (see `global.json` for the required
version). See [COMPILING.md](COMPILING.md) for details. In short:

```sh
git clone <this-repository>
cd Nextendo-Ryujinx
./build.ps1
```

## License

Nextendo's own source is released under the **[PolyForm Shield License 1.0.0](LICENSE.md)** — a
source-available license: you may read, use, modify, and self-host the code, but you may not use it to
compete with the project.

Nextendo is derived from Ryujinx, which is licensed under the **[MIT License](LICENSE.txt)** — the
upstream license and copyright are retained in `LICENSE.txt`, as required. This project also makes use
of code from the libvpx (BSD) and ffmpeg (LGPLv3) projects. See
[distribution/legal/THIRDPARTY.md](distribution/legal/THIRDPARTY.md) for third-party notices.

## Credits

Nextendo stands on the shoulders of the emulator it forks and the projects Ryujinx itself builds on:

- **[Ryujinx](https://github.com/Ryubing/Ryujinx)**, originally created by **gdkchan** and continued by
  the Ryubing community — the entire emulation core.
- [LibHac](https://github.com/Thealexbarney/LibHac) — filesystem.
- [AmiiboAPI](https://www.amiiboapi.com) — Amiibo emulation.
- [ldn_mitm](https://github.com/spacemeowx2/ldn_mitm) — one of the available local-multiplayer modes.
- [ShellLink](https://github.com/securifybv/ShellLink) — Windows shortcut generation.
