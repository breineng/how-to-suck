# EOS SDK snapshot

EOS SDK **1.19.1.2-CL53289219**, extracted from EOS Plugin for Unity **6.1.2**.
Source: https://github.com/EOS-Contrib/eos_plugin_for_unity_upm/tree/0b8f679193c5b6c74df5bddbe3248c9d7aadaee8
Release tag v6.1.2: 86b8ec05a796df5dc931021684c487fa455d5519 (annotated tag).

Includes the unmodified `Runtime/EOS_SDK` C# bindings, Windows x64 EOS native library, and their license notices. The native plugin import settings are retained from upstream. Editor native loading is owned by the game's EosRuntime.

The upstream Unity managers, samples, overlay, Easy Anti-Cheat, other native platforms, and global build callbacks are not included. This keeps Steam and unconfigured offline builds independent of Epic settings. Do not import the full plugin alongside this package: its SDK would duplicate `com.Epic.OnlineServices`.

EOS usage is governed by the EOS Developer Agreement linked in `Runtime/EOS_SDK/license.txt`. `UPSTREAM-LICENSE.md` applies to the upstream Unity plugin code, not as a replacement for the SDK terms.

To update: obtain a tagged upstream release, replace both the entire C# binding directory and the Windows x64 native library together, retain notices, update this version/provenance, then compile and run the networking checks and a two-computer EOS session.
