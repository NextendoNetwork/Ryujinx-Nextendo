# Just Dance 2022 on Nextendo

Title `0100EA6014BB8000`: JD title/notification DNS endpoints use the existing configured server IP. The shared Ubisoft REST rule
already used by UNO remains unchanged. No wildcard Ubisoft redirect is added.

The built-in patch checks title, main module, exact Build ID and every original hook/padding byte before changing memory. Only the
two reviewed builds receive deployed public trust anchors and exact SDK pins; native X509 verification stays enabled. Conflicting
mods and unknown builds are rejected atomically. No private key, supplied game executable, account, save or unlock override is shipped.

For these exact main builds, NsoExecutable exposes the zero-filled tail of the executable page up to the existing RO boundary.
The title's patch needs those bytes. No NSO on disk is changed or exported; all other builds retain their original TextSize.
This replaces the lab's private text-padding NSO workaround.

Blocking UDP RecvFrom now uses the existing deferred IPC reply loop instead of blocking the shared BSD service thread. The socket
keeps its guest blocking semantics, receive timeout and real payload/empty datagram handling; explicit nonblocking calls stay immediate.
This addresses the observed freeze at the Joy-Con choice. Tests cover actual loopback UDP and exact-build memory guards/layout.

Domestic Switch–Ryujinx and Citron–Ryujinx gameplay was confirmed with the dedicated v1.10.1 build carrying the UDP fix and private
padding workaround. Switch–Citron shared scoring against the deployed backend is confirmed. The current upstream integration and
automatic page-tail replacement still require CI and a release smoke test before broad rollout.

Matching reproducible ARM64 source, public anchors and IPS hashes are in the companion Prelude PR under tools/jd22. Existing lab users
should remove obsolete JD TLS mods and the old private text-padding mod before testing this implementation. Original song objectives
remain intact; extra Unlimited content, phone scoring and all optional WDF modes are not implemented by this change.
