# AOT warning cleanup implementation plan

> Execute inline using the existing approved architecture and systematic-debugging / verification-before-completion workflows.

**Goal:** publish the existing app and behavior harness with zero AOT/trim warnings, without suppression, preserving real Whisper/Qwen inference and portable packaging.

**Architecture:** build pinned managed binding sources locally; preserve the exact native NuGet runtime packages. Restrict upstream behavior patches to AOT-safe library search and reflection-free JSON converters. Record source provenance, complete patch artifacts and update instructions.

- [x] Reproduce production publish failure with AOT/trim warnings treated as errors; retain baseline log.
- [x] Add pinned, licensed source projects for Whisper.net 1.9.1 / LLamaSharp 0.30.0, with explicit dependencies and no upstream binary-download targets.
- [x] Add converter regression cases that fail with the existing reflection-based readers under disabled reflection.
- [x] Replace Assembly.Location fallback searches with AppContext.BaseDirectory; replace generic reflection-based converter serialization with typed JSON operations, preserving supported representations and validation.
- [x] Require zero AOT/trim warnings in future publishes. Build managed and AOT; exercise the patched converters and native inference from an executable-only directory.
- [x] Independently review the patch; update evidence and stage a verified release without interrupting the running app.

Preserve defaults, prompts, user config/session, inference versions, runtime cache validation, and user exclusion of paid OpenAI Platform. No credentials or live cloud calls are needed for this work.


The first generated-context patch failed a real-serializer frozen-options regression identified in review; contexts now bind an options copy. Full managed suite: 42/42. Library-wide native-publish flag inheritance exposed unused upstream source API warnings; the binding is explicitly IL-only, preserving its unsupported optional APIs. A negative native entrypoint calling ChatHistory.ToJson failed on the exact expected ILC IL2026/IL3050 errors, proving strict reachability analysis remains enforced. App publish succeeded with zero warning/error lines.

Final verification: app/test strict publishes each have zero warning/error lines. 42/42 managed and AOT checks passed; standalone converter, C++ dependency and HUD checks passed. Real Qwen and Whisper inference passed from an executable-only directory. Profiling recorded separately without claiming performance targets met. The portable copy matches the staged binary; launcher-ready marker is created after verification.
