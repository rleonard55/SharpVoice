# Bundled Whisper Tiny Q5 English Implementation Plan

> For agentic workers: use superpowers:executing-plans to implement and verify inline.

Goal: Default Windows installer, portable ZIP and update package include verified Tiny Q5 English. A clean installation can transcribe without downloading a model. Other models remain on demand; existing saved choices and valid caches win.

Approach: Ship `models/ggml-tiny.en-q5_1.bin` beside the app rather than embedding another compressed resource. ModelManager validates its pinned SHA-256 before returning it, after checking the existing user cache, and retains normal download recovery. Build downloads into ignored staging, verifies before packaging and includes the OpenAI model MIT notice. Model binaries never enter Git history.

- [x] Add offline regressions for bundled-model resolution, cache priority, invalid/missing bundles, other-model fallback and cancellation. Observe failure, then implement resolver with isolated test directories.
- [x] Package the hash-pinned upstream model by default, add license attribution and bump the next release to 0.1.1 without changing published 0.1.0 assets.
- [x] Verify native packaged model resolution with network disabled and isolated storage through non-recording release diagnostics; check archive hashes and install/update lifecycle.
- [x] Update quick start, release notes and packaging docs; run checks and privacy scans; push and build the next GitHub release. Verify public default downloads point to the model-included version when published.

Constraints: No microphone, saved account, user configuration or running user app in verification. Test model payloads remain ignored; unit checks use synthetic bytes and an internal dependency-injected hash resolver, while release checks always use the real pinned model hash. Maintain existing model choices, verified cache behavior, cancellation and atomic network downloads. Updates remain idle gated.

Verification: new bundled-resolution and corrupt-cache checks failed before adding lookup; full offline suite now passes 179/179 with zero build warnings/errors. Upstream model download independently verified: 32,166,155 bytes, pinned SHA-256 c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b. Native packaging and independent read-only review are in progress.

Local native 0.1.1 build and isolated installer lifecycle passed. Staged and unpacked portable diagnostics resolve the real included model from an empty temporary cache with zero HTTP requests. Portable ZIP and full update package both contain the exact pinned model and MIT notices; all asset checksums match, no unwanted configs/credentials/symbols are included. Read-only independent review found no actionable issues. Source-change secret scan passed. GitHub build/draft and default-release publication remain.

Completed: GitHub Actions run 41478153350 passed native build/full offline checks, isolated lifecycle, real portable offline model proof and seven-asset upload from b12aa4b7ebf74edefe97c7d8527c57f5695048a8. Compared every uploaded asset digest with SHA256SUMS, then published v0.1.1 as latest to fulfill the requested default distribution. Anonymous API/feed and both latest/download redirects verified 0.1.1. Setup is 69,276,254 bytes; portable ZIP is 61,233,760 bytes. Published 0.1.0 remains unchanged.
