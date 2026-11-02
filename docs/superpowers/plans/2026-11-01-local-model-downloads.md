# Local Model Downloads and Initial No-AI Default

> **For agentic workers:** Use superpowers:executing-plans inline, with one final fresh independent review.

**Goal:** Fresh installs default to no AI cleanup, retain saved choices, and allow choosing/downloading local AI models with progress.
**Architecture:** Core catalog holds two pinned Qwen 2.5 GGUF choices. A shared Refiner model store verifies and atomically installs downloads with typed progress, cancellation and bounded writes. Embedded inference resolves the selected ID and shares the store. Native AI & models shows a local download action/progress only for Local AI. Existing defaults/persistence and provider routing remain otherwise unchanged.
**Tech Stack:** Existing C#/.NET10/Win32/HttpClient/SHA-256, Native AOT. No dependencies.

## Constraints
- New/missing config backend is none; explicit saved backends/models, including none, survive save/load.
- No actual user config/session edits or real weight downloads in development checks.
- Two supported models: existing 0.5B Q4_K_M and 1.5B Q4_K_M. Official HF LFS hashes/sizes verified; Auto resolves to 0.5B.
- No save/activation just by downloading. Apply retains standard persistence behavior.
- Download progress distinguishes verification from transfer, with bytes/% and a native bar. Cancel supported, failures actionable, retry safe.
- HTTP streaming, bounded length, exact size/hash verification and same-directory atomic replace. Unique temporary file and cleanup; concurrent download/inference share per-path exclusion.
- User changing provider/model or app shutdown cancels/invalidate local download; no stale results for another model.
- Keep UI responsive; track worker through shutdown. Test Connection and Download cannot race resource ownership.
- Downloads continue across Settings close/Apply, tab navigation and dictation; cancellation remains explicit. Connection tests still refuse implicit downloads. Ordinary local inference retains preparation behavior and uses selected model.
- Preserve native theme, contextual help, keyboard Enter/Space safety, other settings and live running app.

## Tasks
- [x] RED initial no-AI/persistence and model-store transfer/cancel/integrity tests.
- [x] Implement catalog, model store, selected inference routing and defaults.
- [x] Native download UI/app wiring and isolated native selection/progress/cancel tests.
- [x] Full managed checks, settings round trips and light/dark preview inspection.
- [x] Final independent source review; reproduce/fix material issues.
- [x] Strict AOT checks, docs and verified staged/portable executable.

## Ledger
- User specified fresh-install behavior and explicit download UI; routine compatible model choices proceed within authorization.
- Official HF metadata read-only check: 0.5B size 491400032, SHA256 74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db; 1.5B size 1117320736, SHA256 6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e.
- No Git commits/staging: workspace is untracked under parent repository.

- UX choice: downloads continue in the background across tab navigation, Settings close/Apply and dictation. Cancelling a large download just because Apply closes the form would waste progress. Model/provider changes still invalidate/cancel the old selection, and app exit cleans up.

- Managed first pass: 85/85 checks and isolated native local/connection/help lifecycle checks passed. Default/custom settings round trips passed; a larger-model friendly-choice Apply assertion and selected-inference metadata/unknown-length integrity coverage were added for final validation.

- Final managed validation: 86/86 checks; native default/custom Settings and isolated local/connection/help checks pass. Both theme previews inspected. Independent source review found no material correctness or regression findings. Actual weight loading/inference and real-network transfer remain manual acceptance; fixtures use bounded small payloads.

- Final delivery: strict app/test AOT publication clean; 86/86 AOT behavior checks, native local/connection/help and both Settings round trips pass. Stage/portable SHA-256 db4fd51d0bf23c22a4281f2c66d68a1064b94bb0e8526032f25b2e4c096f6057, 13976576 bytes. Portable updated only when not running. No app launched/stopped.
