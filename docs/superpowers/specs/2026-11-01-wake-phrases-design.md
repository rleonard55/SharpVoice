# Local Wake Phrases

Date: 2026-11-01. Status: user accepted the approach and supplied replacement default phrases on 2026-11-01; implementation planning in progress.

## Intent and agreed behavior

Allow hands-free dictation and Ask AI without making the user wait between a wake phrase and the request. The user explicitly chose separate phrases, continuous speech, and a dedicated local detector. Wake listening is optional and disabled on install and when migrating existing configurations. On Hotkey remains the normal microphone default.

User-selected defaults: **Hey SharpVoice** starts Ask AI; **Hey SharpVoice Dictate** starts ordinary dictation. Both are editable English phrases. These intentionally overlap: the longer phrase takes precedence, and “Dictate” immediately following “Hey SharpVoice” is reserved for switching to dictation. Wake-owned recordings stop after **2 seconds of silence**, adjustable from 1–10 seconds (implementation recommendation). Existing maximum recording duration, stop controls and cancellation remain effective. Hotkey recordings retain their configured Hold/Toggle and silence behavior.

## Approaches

1. Dedicated local keyword spotting: selected by the user. Additional runtime/model packaging, but no repeated idle Whisper decoding. Use sherpa-onnx's custom-keyword C API, with a small English keyword model and a CPU worker.
2. Rolling Whisper detection: considered and declined. Reuses an engine but performs repeated general transcription while idle, competing with drafts/final transcription and increasing latency and CPU use.

Sherpa supports editable keywords without retraining, with phrase tokens generated from its model vocabulary. Its published English model includes the BPE vocabulary required for tokenization. Sources: [custom keyword spotting](https://k2-fsa.github.io/sherpa/onnx/kws/index.html), [published models](https://k2-fsa.github.io/sherpa/onnx/kws/pretrained_models/index.html), [C API](https://k2-fsa.github.io/sherpa/onnx/c-api/html/keyword_spotting.html).

## Settings and feedback

Add a compact Wake Phrases section reachable from Audio settings. Show an Off/On choice, the two phrase fields, silence duration, and readiness/download status. Use existing themed controls, hover behavior and on-demand help. The microphone privacy consequence must be visible beside the enable choice: **Wake phrases keep your microphone listening locally.** Longer explanations belong behind help.

Enabling automatically prepares the model with download progress and cancellation; no Python, pip, external setup button or account is required. Saving starts preparation asynchronously; it must not freeze settings or block ordinary hotkeys. Failure leaves wake activation unavailable with a clear Retry action, while normal hotkeys remain usable. Readiness must reflect the running service, not just a saved On value. A startup with wake enabled restores preparation/listening automatically.

Wake listening overrides On Hotkey's closed-idle-microphone behavior only while explicitly enabled and ready. Disabling releases that demand; Keep Ready and Audio meter preview may independently retain their existing microphone demand. Pausing from the tray suspends all microphone capture, including wake listening. The existing neutral tray icon remains neutral while idle; its tooltip/status identifies Listening for Wake Phrases, Paused, Preparing or Unavailable. Active recording/error colors and existing chimes remain. The HUD uses current live transcription and shows the started mode plus the silence-stop hint; do not permanently add another floating window.

Validate nonempty phrases, a bounded length (2–6 display words, at most 80 characters), supported English letters/apostrophes/spaces, and distinct normalized phrases. Support a complete word-prefix relationship, as required by the selected defaults; use longest leading match precedence. Normalize SharpVoice to the detector's spoken words “sharp voice” while retaining the branded display spelling. Do not silently accept unrepresentable tokens. Keep threshold tuning internal initially; silence duration and phrases are user settings.

## Architecture

### One microphone and bounded audio ownership

Reuse WasapiAudioRecorder. Add wake demand alongside recording, Keep Ready and meter-preview demand; do not create a second microphone session. Send 16 kHz mono samples to an owned, bounded detector queue before destructive noise gating. Never perform inference or await work on the WASAPI callback. Preserve current filtered/gated recording behavior separately. Queue overflow resets detector history instead of interpreting discontinuous audio as one phrase.

Keep an **8-second maximum** in-memory audio ring with absolute sample positions and a capture epoch. A detection includes mode, matching phrase, onset sample and epoch. On the UI thread, accept it only while Idle, unpaused, speech input permitted, and still matching the current configuration/device epoch. Reserve that operation before asynchronous selection capture or model preparation. Repeated/stale detections cannot start another operation.

With overlapping phrases, the short match reserves a provisional session and starts preserving audio immediately; it does not yet commit to Ask AI. Continue the detector only to recognize the matching longer phrase from the same utterance onset (within 400 ms of the short phrase onset), never to create a second session. Final transcription resolves the longest leading phrase before any mode-dependent routing, provider validation, conversation reservation or LLM call. A confirmed longer detector match also selects dictation when Whisper omits its phrase; a full longer transcription match wins over a provisional short match. Otherwise use the detected short mode. A later occurrence of “Hey SharpVoice Dictate” in the request body cannot switch modes. Initial feedback can say Listening until resolved. Both modes capture the original target/selection at the first activation; selected-text behavior and Connected Ask routing are decided only after resolution. No-provider validation must therefore occur after resolution for these provisional sessions, so transcription-only dictation still works with AI disabled.

Atomically seed the recording from the bounded ring at the detected onset (with approximately 200 ms safety padding) and attach subsequent samples using absolute cursors. Selection/window work cannot create a hole between pre-roll and active audio. No duplication, stale samples or samples from a previous device are allowed. If the detector is too far behind and the required audio has expired, discard that activation and report a retry cue rather than inserting truncated instructions. An 8-second ring is a ceiling, not permission to retain unrelated idle speech in prompts.

Retain the phrase in the captured audio rather than cutting at an unreliable final-token timestamp. Remove only the recognized phrase at the beginning of live/final transcription, allowing case, punctuation and “SharpVoice”/“sharp voice” spelling variants. Do not remove later occurrences, arbitrary matching substrings or the user's first request words. If Whisper omits the phrase, retain the request intact. Perform prefix cleanup before no-speech handling, history, context, routing or provider calls. Wake phrase alone, or [BLANK_AUDIO], produces no insertion, provider request or conversation turn. Existing warning behavior may signal an empty request; do not substitute explanatory text.

### Detection and runtime boundary

Introduce a small detector abstraction emitting phrase IDs and sample timestamps; native sherpa interop stays in a separate implementation. A single sequential worker owns streams/native handles and drains before their destruction. Package pinned Windows x64 native libraries through the existing verified NativeRuntimeBundle. Do not dynamically fetch executable helpers. Add license notices and record versions/hashes.

Use the published English 3.3M keyword model, preferring its quantized CPU encoder. Download only the required pinned model/vocabulary assets into a dedicated per-user model directory. Stream with size bounds, cancellation, pinned hashes and atomic promotion. Archive extraction, if needed, must allow only expected bounded entries and reject traversal. An interrupted/corrupt download is never reported ready.

Editable phrases require exact model-compatible SentencePiece encoding. Before integrating the service, the implementer must prove native AOT loading, phrase tokenization and detection against prerecorded fixtures. Choose a bundled tokenizer compatible with the published bpe.model (native SentencePiece or a proven AOT-compatible implementation); no approximate word-to-token splitting and no user-side Python. Pin and test that component. If the model/runtime cannot meet these requirements, report the evidence and revisit the detector choice rather than silently falling back to continuous Whisper. The exact library binding, native release and verified hashes are implementation decisions. Implementation verification established that this file contains a UNIGRAM model despite its bpe.model filename; the managed restricted-English tokenizer matches 306 independent upstream SentencePiece reference phrases.

### Session and permission integration

Wake triggers enter the same coordinator as the two general hotkeys, with an explicit Wake origin. Dictation retains selected-text editing, per-app rules and AI minimum-word policy. Ask AI retains configured provider, Smart Ask routing, conversation memory and Connected Ask permissions. No editor bridge/protocol change is needed: specialized code commands continue to start through the extension and retain previews. Wake detection alone must not collect document/selection context or invoke tools.

Resolve the target and effective settings at activation, with existing foreground/selection validation before insertion. Reject unsupported/SharpVoice-owned targets using existing rules. A wake trigger while settings or another owned popup has focus must not insert into that popup. If Ask AI has no usable provider/Connected path, show the existing helpful configuration error without beginning an unusable recording.

Hotkey release must not stop a wake-owned recording. Existing explicit stop/cancel controls remain available; pressing a matching hotkey during a wake-owned recording should stop it once and consume that key's later release. A different mode shortcut must not start a concurrent operation or change the accepted wake request. These origin rules must cover Hold and Toggle.

For wake sessions, run silence observation independently of the hotkey gesture and use the wake-specific duration. Initialize observation from the latest audible samples already in the seeded request, so a fast complete request is not lost. Empty phrase-only recordings end after silence too. Ambient noise can postpone silence completion, so the existing maximum duration always applies and Stop remains available. Quiet speech uses the configured recording noise threshold; this change must not raise it.

Suspend/reset detection and clear pending idle audio during processing, editor-owned recording, reply recording, pause, microphone restart, config replacement, shutdown, and spoken playback. During ordinary recording suspend it too, except the bounded same-onset longer-phrase confirmation described above for a provisional wake session; disable that confirmation once the onset falls outside the bounded ring or the longer match is confirmed. While suspended for playback, microphone privacy and SpeechInputGate behavior remain authoritative. Resume only when Idle, speech is permitted and a fresh device epoch is ready. Add a short post-output rearm guard and fresh stream so SharpVoice's own start/stop/success cues and TTS cannot become wake commands. Wake settings/device changes cancel/drain the old worker before disposing handles; late callbacks are generation-checked.

## Scope and limits

This is English wake activation, not room transcription, speaker identification, a cloud listener, voice authentication, or a new tool approval mechanism. Audio remains bounded and in memory; idle detection never adds to history or logs audio/text. Existing optional dictation history behavior applies only to completed requests. Always-listening has a resource/privacy cost and cannot be represented as a closed microphone.

Do not claim physical-device accuracy from synthetic tests. TV/background speech can trigger a detector; editable distinctive phrases and explicit opt-in help, but no speaker authorization is promised. Refactor previews and Connected Ask change approvals stay on screen as already required.

## Verification and delivery

Add meaningful tests for config defaults/migration/validation, both phrases, overlapping-prefix precedence and no premature Ask AI request, prefix removal without damaging request text, sample ownership and gapless handoff, bounded overflow/stale epochs, single activation, hotkey origin interactions, pause/TTS cancellation, silence/max-duration stop, no-provider and empty-request paths, failed/canceled downloads and lifecycle disposal. Exercise the actual detector with prerecorded positive/negative audio including quiet speech, both continuous phrase-plus-request utterances, and phrase-only audio. Test native AOT loading and tokenizer equivalence; measure idle CPU/memory and activation delay on the test machine and report actual numbers, not an unmeasured latency promise.

Run managed and Native AOT suites, themed native settings checks and an isolated end-to-end prerecorded capture fixture through the real service. Verify hotkey On Hotkey still closes idle capture with wake disabled. Bundle and stage the matching standalone app with notices and README/smoke-test updates. Do not restart the user's app or use their microphone, clipboard, editor, account or external actions in automated tests. Real-room false activations, quiet-voice reliability and chime audibility remain explicit manual acceptance after staging.
