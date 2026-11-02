# Performance measurements

## Provider update measurements — 2026-11-01

Latest standalone AOT build on the same i7-1165G7 Windows machine. The app sample below was taken after compilation finished; an earlier launch during compilation was excluded from these figures because of contention. These are short single-machine samples, not cold-start or sustained-resource guarantees.

| Measurement | Result |
|---|---:|
| Process entry | 23.91 ms |
| Desktop initialization after entry | 115.62 ms |
| Microphone/model preparation after entry | 206.66 ms |
| App idle CPU, one-core percentage over 5 s | 1.246% |
| App working set before / 2 s after trim | 61.80 / 1.78 MiB |
| App private committed memory after trim | 45.73 MiB |
| Capture harness readiness | 24.12 ms |
| Capture idle CPU / working set | 0.000% rounded / 15.27 MiB |
| Tiny Q5 model load | 111.79 ms |
| JFK inference / repeat after trim | 921.79 / 1600.13 ms |

The paused capture harness sampled 0.625% CPU; its short sample includes other process cleanup. Rounding an individual sample to zero does not establish zero background work. The app diagnostic disables shell integration and does not load Whisper weights. No microphone audio was saved. Cloud/Claude latency is not included; Claude Code starts a separate process per request. No speed improvement is claimed from this provider change. The original startup/turnaround/sustained-memory/zero-CPU acceptance targets remain open.

Raw files: `artifacts/performance-provider-update-2026-11-01.txt` and `.build-cache/claude-app-performance-idle.txt`.

## Warning-free build — 2026-11-01

Same Intel i7-1165G7 / Windows machine; fixed source bindings, unchanged native runtimes. Standalone AOT runner passed real Whisper inference. This is a new short sample, not a controlled performance comparison or evidence that warning cleanup caused a speedup.

| Measurement | Result |
|---|---:|
| Actual app entry | 33.61 ms |
| App desktop initialization after entry | 109.04 ms |
| App microphone/model preparation after entry | 296.92 ms |
| App working set before / 2 s after trim | 55.76 / 1.84 MiB |
| App private committed memory after trim | 42.57 MiB |
| Capture runner readiness | 28.75 ms |
| Capture runner idle CPU, one-core percentage over 5 s | 0.000% |
| Tiny Q5 model load | 98.45 ms |
| JFK inference / repeat after trim | 888.00 / 1242.41 ms |
| Inference-process working set before / after trim | 60.41 / 2.72 MiB |
| Offline Qwen, full master prompt | 43.08 s |

The app diagnostic disables shell integration and loads no Whisper weights. Its first-five-second CPU sample was 0.312%; the separate capture runner rounded to zero. These are different workloads. Ready-to-dictate startup, universal <350 ms turnaround, sustained residency and literal 0% CPU remain unaccepted. Cloud refinement/insertion are not included in local inference timings. No microphone audio was saved.

Raw capture/Whisper output is `artifacts/performance-2026-11-01.txt`; actual-app diagnostic output is `.build-cache/app-performance-2026-11-01.txt`.

## Previous measurements — 2026-10-31


Machine: Intel Core i7-1165G7 (4 cores / 8 logical processors), Windows 10.0.26300. Native AOT win-x64/x86-64-v3. These are short, single-machine samples, not latency guarantees or long-running acceptance.

## Actual app startup diagnostic

The published app's `--diagnostics <report.txt>` mode uses saved configuration, DPAPI auth initialization, native desktop controls and the production coordinator/capture/model verification. It disables the extra tray icon and global hook, records no dictation and inserts nothing. Whisper weights are not loaded until key-down.

| Measurement | Result |
|---|---:|
| Process creation to managed/native entry | 36.55 ms |
| Entry to configuration/auth/desktop initialization | 123.77 ms |
| Entry to microphone and cached-model preparation | 398.14 ms |
| Process CPU during the first 5 seconds after preparation, one-core percentage | 0.000% |
| Working set before trim | 56.06 MiB |
| Working set 2 seconds after trim | 1.78 MiB |
| Private committed memory after trim | 42.61 MiB |

The first five seconds can contain startup cleanup. Shell integration is disabled, so this is an instrumented startup measurement, not a full production-launch equivalence claim. A process entry under 40 ms does not meet a ready-to-dictate startup under 40 ms.

## Capture and inference harness

`powershell -File scripts/measure-performance.ps1 -Tests .build-cache/standalone-check/SharpVoice.Tests.exe` completed successfully. The runner directory contained only its AOT executable; the external JFK WAV fixture and model were explicitly supplied. No microphone audio was saved.

| Measurement | Result |
|---|---:|
| Capture readiness | 128.53 ms |
| Capturing idle CPU, one-core percentage over 5 s | 0.000% |
| Capturing idle working set / private memory | 15.04 / 5.70 MiB |
| After recording and trim CPU / working set / private memory | 0.625% / 1.92 / 14.08 MiB |
| Paused CPU / working set | 0.000% / 2.41 MiB |
| Tiny.en Q5 first load, including bundled runtime initialization | 109.38 ms |
| JFK fixture transcription | 1475.38 ms |
| Inference-process working set before / after trim | 60.44 / 2.69 MiB |
| Repeat inference after trim | 1398.72 ms |

Earlier runs before the final C++ bundle measured 161.74 ms model load and 731.46 ms capture readiness. Other short samples varied: capture readiness 139.81 ms and CPU rounded to zero. Continuous pre-roll still requires capture processing; the application does not satisfy a literal zero-background-CPU guarantee. Working-set trimming pages memory out; it does not prove lower committed memory or sustained residency below 20 MiB.

## Optional offline refinement

Qwen2.5-0.5B-Instruct Q4_K_M completed a fixed transcript with the full master prompt in **46.70 seconds under Native AOT with the final bundled C++ dependencies**, from the executable-only directory. The model left the sentence unchanged. This proves local loading/inference, not broad editing quality. It consumes substantially more transient memory than Whisper; context and weights are released per request.

## Acceptance

- Standalone executable size is recorded in `verification.md`; it is below the specification's approximate 25 MB budget. Models remain separate downloads.
- Universal <350 ms dictation turnaround, <150 ms model reload, <40 ms ready-to-dictate startup, sustained <20–25 MB residency and literal 0% capture CPU are **not accepted as met**.
- Cloud refinement and text insertion timing have not been included in the local Whisper timings above.
- Repeat the measurements on an otherwise idle machine and exercise complete real dictations before drawing production conclusions.
