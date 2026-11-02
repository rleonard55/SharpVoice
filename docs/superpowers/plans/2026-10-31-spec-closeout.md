# Specification closeout

Approved scope: finish Data/Spec.txt, preserving later user choices. OpenAI Platform explicitly excluded by user on 2026-10-31; ChatGPT OAuth remains default.

1. Establish reproducible Windows/AOT measurements for capture readiness, idle resources, Whisper load and inference; distinguish startup, model load, and release-to-insert.
2. Remove recording-list growth and final audio copies with owned pooled memory; verify cancellation, disposal, pre-roll and quiet speech. Keep recorder synchronization correct rather than claiming lock-free capture without evidence.
3. Preload Whisper during recording; add verified quantized choices to settings/tray and exercise actual inference.
4. Complete HUD rendering and memory cleanup with Windows native APIs, preserving fallback behavior and themed UI.
5. Investigate optional embedded LLM and single-executable packaging against AOT constraints. Implement only working supported paths; document measurable limitations explicitly.
6. Build managed and Native AOT, run behavioral and Windows integration checks, stage a verified update, and update the requirement/evidence ledger.

Performance numbers are acceptance targets, not guarantees. Do not suppress upstream AOT warnings or call targets met without measurements. No personal audio is saved by diagnostics.

## Execution evidence

- Completed: pooled owned capture buffers, concurrent versioned pre-roll snapshots, quiet pre-roll counting, Whisper preloading, all six verified model choices.
- Completed: optional LLamaSharp/Qwen refinement with atomic verified download, cancellation/output-limit guards, CPU-only bundled libraries, per-request context/weights disposal.
- Completed: Direct2D/DirectWrite HUD with themed native pixel checks and GDI fallback; supported post-insertion working-set trim.
- Completed: compressed native runtime resources, hash-verified per-user extraction, executable-only inference checks, embedded third-party license notices, staged portable build.
- Completed: startup/resource diagnostic mode; repeatable capture/Whisper measurements; managed and AOT behavioral/native checks. See docs/performance.md and docs/verification.md.
- User excluded paid OpenAI Platform backend; OAuth stays default.
- Acceptance still open: upstream warning cleanup, literal no-runtime-files/static-link interpretation, exact hardware packet cadence, strict latency/CPU/residency targets, and physical/interactive/long-running Windows QA. These have not been marked complete.

Review: an independent read-only source review checked ownership/cancellation, preload shutdown, COM layouts, and bundle loader layout. It found no Important/Critical issues. A minor shutdown error-propagation issue was corrected. Executable-only testing then caught and corrected LLama ModelParams triggering native initialization before loader configuration; the repeated real AOT inference passed.
