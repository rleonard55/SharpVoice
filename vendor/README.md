# Pinned managed inference bindings

SharpVoice builds the managed bindings from these exact upstream commits so its AOT fixes are reviewable and independent of machine-local NuGet cache modifications:

| Project | Release / commit | Download archive SHA-256 |
|---|---|---|
| LLamaSharp | 0.30.0 / `d03b6cbca3c66357e2ed9ee9d0324f5eaeabfd4f` | `38ce611b8f54a103210d69ae64cb193d8036411cd663989cf2161573d796d694` |
| Whisper.net | 1.9.1 / `98278acc38ae23590cdfa9859f78f089abae52a7` | `1fc07bd989b5667ebd2001402b60078f501bce768709e2004561b3294408adea` |

Sources: https://github.com/SciSharp/LLamaSharp/tree/d03b6cbca3c66357e2ed9ee9d0324f5eaeabfd4f/LLama and https://github.com/sandrohanea/whisper.net/tree/98278acc38ae23590cdfa9859f78f089abae52a7/Whisper.net . Each directory retains its upstream MIT license. Only managed `.cs` sources are imported; native binaries still come from the pinned NuGet runtime packages.

`upstream-files.sha256.json` records the original bytes of every imported source. `aot-fixes.patch` records all modifications to those files; added source files and replacement project files are ordinary reviewable files. The local project files target net10.0 only and omit upstream packaging, multi-platform builds and binary-download targets. Dependency versions match upstream; dependencies already supplied by net10.0 need no redundant package reference.

## Supported AOT scope

LLamaSharp does not declare its complete upstream API AOT-compatible. Its optional state/chat persistence APIs still use reflection and are outside SharpVoice's supported call paths. The local project preserves that distinction (`IsAotCompatible=false`) and produces managed IL (`PublishAot=false` as a local library property), rather than advertising library-wide compatibility. **The app and test entry points still run full publish-time AOT/trim analysis with warnings treated as errors.** There are no AOT warning suppressions. Calling a newly reachable unsupported API must fail that gate. The intentionally unsupported `tests/SharpVoice.AotGuard` entrypoint verifies this: its `ChatHistory.ToJson()` call must fail strict native publish. The library setting does not disable entry-point ILC analysis.

The patches make native discovery independent of assembly file locations, and switch the model-parameter converters used by the supported graph to generated JSON type metadata while preserving caller serializer options. They do not change model inference, native ABI, model versions or CPU selection.

## Updating

1. Download an immutable upstream commit archive; verify its hash and NuGet provenance.
2. Replace the imported managed sources and licenses, excluding bin/obj/build outputs; regenerate the original-file hash manifest.
3. Reapply/review `aot-fixes.patch` and retain the added `ModelParamsJsonContext.cs`, or remove fixes upstream has adopted.
4. Keep explicit project dependency versions aligned with the chosen release and native runtime packages.
5. Publish app and tests under strict AOT/trim analysis; run converter regressions, the complete behavior suite, standalone native dependency loading, Whisper and Qwen inference before staging an update.
