# Bundled Edge Voices Implementation Plan

> Execute inline with superpowers:executing-plans, then fresh final review.

**Goal:** Edge voices work without user setup.
**Architecture:** Embedded pinned runtime archive, verified per-file extraction, existing speech process transport.
**Tech Stack:** C#/.NET Native AOT, Python embeddable x64, edge-tts.
**Spec:** docs/superpowers/specs/2026-11-01-bundled-edge-voices-design.md

## Constraints / Review Focus
No system Python/PATH changes; no actual user settings or live speech during verification. Pin upstream hashes/licenses. Bounded archive extraction, atomic repair, concurrent preparation and cancellation. Keep Windows fallback.

- [x] Add failing bundled-runtime and settings tests; run RED.
- [x] Add reproducible pinned archive packaging, private extraction and interpreter dispatch; remove setup button/manual instructions.
- [x] Run managed/AOT checks, native UI and offline real-helper probe; review and fix findings.
- [x] Update documentation and guarded staged/portable review build.

Ruling: user explicitly requests in-app setup; packaging details are implementation decisions, no repeated approval round. Existing repository is untracked; no Git mutations. Bundle instead of download-on-user-machine to eliminate setup failure and extra work.

Evidence: initial stub run failed with 109/110 passing (`bundled-edge-red-checks.log`). First green managed110/110 and hidden-desktop UI passed. Pinned archive rebuild repeated with identical SHA-256 `1ef8424c2ede1324e55c0ed88d9bb0d4e70f3cbe3bfe856b09bfa1d7ec8fd45b` (282 files, 17 license files, 12,048,217 bytes). Official embeddable runtime includes both VC runtime DLLs; no system Python/VC installation is needed for this helper.

Fresh read-only review caught isolated Python ignoring PYTHONIOENCODING. Fixed through shared ConfigureInterpreter with -X utf8, plus real offline Unicode pipe regression. -B and unique temporary pycache_prefix prevent loading mutable cached bytecode beside verified sources. Reviewer rechecked and found no further concrete issues. Final suites/publications pending.

Final managed build: zero warnings/errors; **110/110** checks passed, including the actual private interpreter version/import and Unicode pipe probes. Native hidden-desktop UI passed with no manual setup button. No live speech, microphone, account or user settings touched. Native AOT tests and app publication follow sequentially.

AOT first run109/110: cancellation fixture caught working-directory removal before the killed child fully exited. Root cause: the original token-bound WaitForExitAsync was canceled, so awaiting Task.WhenAll did not reap the process. Added a bounded uncancelled child-exit wait before draining/returning. This fixes the owned-process lifecycle rather than retrying directory deletion. Final build/checks rerun required.

Build tooling ruling: even Debug/Release share project.assets.json under the artifacts layout. Parallel restore caused NETSDK1047, so rerun Native AOT publication after managed build fully completed; all subsequent builds sequential. This is a restore collision, not a source compilation failure.

Final managed and Native AOT suites both **110/110**, exit0, including real bundled helper imports/version/Unicode, damaged-file repair and process cancellation. Native UI passed on both runtimes. Publications/builds clean; app publication now running alone after test processes exited. Actual online synthesis/audible playback remains a manual check.

Final app Native AOT publication exited0 without warnings/errors (`bundled-edge-aot-app-ready.log`). Staged and guarded portable SHA-256 `6ced5ae1ee99e5c0207a4aff86ec2860a89f9da318a71a268208a827e3e522d8`, matching PDB and update.ready prepared. Documentation updated; no user app started or stopped.
