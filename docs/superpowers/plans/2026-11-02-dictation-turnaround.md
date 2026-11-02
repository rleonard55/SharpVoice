# Dictation turnaround

Goal: bring release-to-insertion as close to one second as practical, retaining selected model, prompts, AI cleanup policy, target validation and complete-result insertion.

Cached public speech and the installed Tiny Q5 model show full Whisper context costs approximately 1–2.3 seconds. Conservative short-context processing with a 768-frame floor, 128 extra frames and up to 6 threads substantially reduces decoding. Changing context is experimental and can change accuracy. Google 3.5 Flash-Lite already defaults to minimal thinking; no reasoning change is needed.

1. Add optional Fast (Short Dictation) in AI & Models. Existing defaults remain Standard. Keep complete input audio and standard processing for recordings longer than 15 seconds; do not alter wake activation/resolution.
2. Apply the mode to foreground and local live-draft transcription, preserving cancellation draining, final full-recording inference and existing refinement/selection/Ask behavior.
3. Measure release-to-insertion dispatch; show total in successful status and a stage breakdown through on-demand help. Retain only the last timing in memory.
4. Verify preferences, decode bounds, UI/themes, cancellation and the full suite. Benchmark the actual engine with alternating Standard/Fast runs on public prerecorded audio, separating inference from total turnaround.
5. Review, publish and stage the native app without restarting the current process or changing saved preferences.


Implementation, managed verification, UI checks, independent review, native publishing and guarded staging are complete. Actual-engine alternating runs showed median Fast decoding around 0.6 seconds on 3/6/11-second public controls; the clipped 6-second control changed words. Keep the opt-in accuracy warning. Native publish/staging verification is recorded in docs/verification.md. No guarantee of sub-second cloud-refined insertion is made.
