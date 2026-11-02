# Native AOT binding fixes — 2026-11-01

## Cause and fix

A strict baseline app publish failed on IL3000 in Whisper/LLama library discovery and IL2026/IL3050 in LLamaSharp model-parameter JSON converters. The ordinary prototype had emitted these warnings without suppressing them.

SharpVoice now builds the managed bindings from their exact release commits under [vendor](../vendor/README.md). Native binaries remain the same pinned runtime packages. Only three imported files are patched, plus one added JSON context:

- Native discovery uses `AppContext.BaseDirectory` rather than `Assembly.Location`, which is unavailable for the embedded managed assemblies in Native AOT.
- Tensor-split reads/writes and metadata reads use generated `JsonTypeInfo` overloads. The generated context receives an options copy, allowing frozen/reused serializer options and preserving number handling, case-insensitivity, and property naming.

The original-source SHA-256 manifest, release archive hashes, licenses, and [exact patch](../vendor/aot-fixes.patch) make the imported sources auditable. To check the patch against the current sources with GNU patch, use `patch --dry-run --binary -R -p1 < vendor/aot-fixes.patch`; `--binary` preserves the upstream Whisper CRLF line endings.

## Verification

- Before the JSON fix, the new metadata regression failed with reflection disabled (41/42 checks passed).
- Review identified a frozen-options regression in the first implementation. A test using actual generated outer serializer metadata reproduced it; the options-copy fix then passed all 42 managed checks.
- The native converter smoke checks metadata/tensor round-trips through actual serializers, reused frozen options, custom number handling, naming policy, and case-insensitive metadata.
- Imported-source manifest comparison identifies exactly the three intended modified files; reverse patch dry-run passes.
- The repaired application published successfully with zero warning/error lines under the strict gate.
- The negative `SharpVoice.AotGuard` entrypoint failed publishing with ILC errors IL2026/IL3050 naming `LLama.Common.ChatHistory.ToJson`, proving reachable unsupported calls remain rejected.
- Final test AOT publish and real inference results are recorded in [verification.md](verification.md).

## Enforcement and scope

`Directory.Build.targets` sets `IlcTreatWarningsAsErrors=true`, `ILLinkTreatWarningsAsErrors=true`, and `TrimmerSingleWarn=false` for the app and test entry points. Newly reachable unsupported code must fail publish. No AOT/trim warnings are suppressed.

The complete LLamaSharp upstream API includes optional reflective chat/state persistence methods that SharpVoice does not use. Its project therefore does not claim library-wide AOT compatibility and compiles as managed IL, rather than inheriting the executable’s native-publish flag. The entry-point publish still analyzes the full reachable application graph under the strict gate. This change validates SharpVoice's supported call paths, not arbitrary use of every upstream API.

This milestone does not establish the remaining performance targets, strict static linking, or interactive Windows acceptance.
