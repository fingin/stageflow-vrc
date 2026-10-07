# Saved StageFlow versions

This standalone local Git repository preserves every saved source revision from
the original StageFlow and StageFlow-v2 repositories. The original folders were
left unchanged. It was assembled at `E:\stageflowvrc` on 2026-10-06.

## Branches

| Branch | Contents |
|---|---|
| `main` | Default checkout: latest v2 source, credits, license metadata and this repository's documentation. |
| `v2` | Same latest v2 checkout at the time this repository was assembled; available for v2 development. |
| `v1` | Frozen v1 at `47bbbe6aa3516ef5173f3402a546f196ffbfabca` (`v0.1.0-alpha.4`). |

The repository is a normal working repository, not a bare server. It has its own
Git objects and no configured remote; it does not depend on the original folders.
No network hosting service or remote publication was set up.

## Release snapshots

All pre-existing tags and commit IDs are preserved. `v0.1.0-alpha.1` was added to
the previously untagged initial commit after verifying its version metadata.

| Tag | Commit | Saved revision |
|---|---|---|
| `v0.1.0-alpha.1` | `3cba0f03b4a4e38e9c6b68e0260fed3760f23fe8` | Initial native GPU playback, grid compositor, MTC and built-in HLS. |
| `v0.1.0-alpha.3` | `bbcb93ee71ea6fdb37011d3f8a82489c18c25970` | Show workflows, GPU media optimization and stress diagnostics. |
| `v0.1.0-alpha.3-package` | `158b69b0def596a0cdbedc6b13d22e7df754311a` | Same app version with pinned portable restore graphs. |
| `v0.1.0-alpha.4` | `47bbbe6aa3516ef5173f3402a546f196ffbfabca` | Menu crash fix and failure diagnostics; final frozen v1. |
| `v2.0.0-alpha.2` | `dfd0f2496403c10ca497a1b64f26b9b29cbcf123` | V2 workspace, preferences and network routing. |
| `v2.0.0-alpha.3` | `dcdefa0063421f6febce64c1598ffdeefbc1cc17` | OSC feedback/mapping preferences and aspect-locked preview. |
| `v2.0.0-alpha.4` | `dd68bbf6a8de1b2526a5a22418ec7a2ce0eff313` | RTMP authentication, encoder settings and OSC ports 8000/8001. |
| `v2.0.0-alpha.5` | `b8cf05553b83cb3056c3394f5877c9374613a315` | Monitor links and per-route encoder/protocol telemetry. |

The following original documentation commit is also preserved:
`1d8666324fce4213676b2bd98b555b6301819839`, adding dependency provenance,
credits and inspiration after alpha.5. The application version remains
`2.0.0-alpha.5`; repository packaging does not claim a new application release.

No separate source snapshot was found for `0.1.0-alpha.2` or `2.0.0-alpha.1` in
the saved repositories, reflogs or source archives. No replacement snapshots or
tags were invented for those labels.

## Open or modify a version

Run these commands from `E:\stageflowvrc`. Commit or stash your changes before
switching versions.

```powershell
# Latest v2, including the repository documentation.
git switch main

# Frozen v1 source.
git switch v1

# Inspect an exact release without moving a branch.
git switch --detach v2.0.0-alpha.3

# Start an independent change from a saved release.
git switch -c my-change v2.0.0-alpha.3
```

Use each revision's own README and build scripts when building it. Historical
tags retain their original dependency pins and documentation. The expanded
credits and dependency audit were added after the alpha.5 release tag; consult
them on `main` alongside the notices contained in the historical revision.

## Source-only contents and verification

The checkout includes application and test source, project files, restore locks,
build scripts, web assets, sample layouts, validation references and license
notices. App icons, a validation image, the NDI license PDF, the JavaScript HLS
player and the native **source build recipes** ZIP are intentional source support
files. They are not packaged StageFlow applications or native runtime libraries.

Compiled applications, native DLLs, debug symbols, NuGet package archives, SDK
installations and `bin`, `obj`, `runtime` or `artifacts` directories are excluded.
The build scripts fetch or locate dependencies separately; consult
[the main README](../README.md#build-and-modify) and
[the dependency inventory](DEPENDENCIES.md). Third-party license notices remain
intact under their original terms.

All four previously saved v2 source ZIPs were compared with `git archive` exports
of the matching release tags. Every file matched byte-for-byte:

| Saved source archive | Matching files | Differences |
|---|---:|---:|
| `StageFlow-v2-Source-2.0.0-alpha.2.zip` | 173 | 0 |
| `StageFlow-v2-Source-2.0.0-alpha.3.zip` | 176 | 0 |
| `StageFlow-v2-Source-2.0.0-alpha.4.zip` | 181 | 0 |
| `StageFlow-v2-Source-2.0.0-alpha.5.zip` | 186 | 0 |

The archives are represented by their Git snapshots instead of duplicate ZIP
copies in this repository. All nine original commits are retained. No application
behavior was changed when assembling this repository.
