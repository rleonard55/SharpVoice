> Voice packaging amended by the user: see `2026-11-01-bundled-edge-voices-design.md`. Manual helper setup described below is superseded.

# Smart Ask AI and Spoken Answers

The user requested an optional Ctrl+Alt+Shift behavior: questions display a response pill and are read aloud, while writing/edit requests still insert. The user chose Edge voices with a Windows offline fallback after the response-pill proposal. This implements that accepted behavior; feature defaults off and saved preferences persist.

## Routing

When enabled for ordinary Ask AI, the existing selected AI provider returns one strict JSON object containing `route` (`answer` or `insert`) and `text`. Determine intent and generate content in the same request. “Could you rewrite this?” and “Output Invictus” insert; “What does this mean?” answers. Selected text is context, never a routing instruction. Uncertain requests/clarifications use the answer pill; invalid JSON fails without pasting. Suppress raw envelope streaming. Standard dictation, selected-text dictation edits and explicit VS Code preview commands retain their existing paths. Answers never automatically paste or replace selected text.

## UI and Preferences

General settings has a Smart Ask AI button opening an owned preferences window with Off/On and an Edge voice choice, preserving room on the main page. Help explains online text sharing and optional helper setup. Use an interactive rounded response window separate from the noninteractive recording HUD. Display full response text with scrolling, muted model/status and Stop/Replay/Copy/Insert/Close. Keep the response until dismissal or another recording, with no forced focus on display. Copy is explicit. Insert validates the captured original window/process identity, reactivates it and uses the existing control/selection validation; if it no longer matches, refuse and retain Copy. No silent destination change. Preserve response/result text in the existing history/preview policy, without storing audio.

## Speech

Prefer the maintained external `edge-tts` helper found on PATH/standard per-user executable locations. This avoids embedding an unofficial, changing Edge service protocol or a Python runtime in the Native AOT app. Send answer text over stdin, never process arguments or plaintext files. Receive bounded MP3 audio, play through Windows media support and delete the app-owned temporary audio after playback. The helper is optional: absent/failing Edge synthesis falls back to installed Windows SAPI speech, with visible status. No separate paid speech API/account or automatic helper install. Setup help links the upstream helper documentation; `pipx install edge-tts` is a user action. Default Edge voice is en-US-AriaNeural; offer a few known English alternatives. This dependency choice is the implementer's call.

Both engines support cancellation. Explicit Stop/Close, pause, exit, disabling the feature and new recording cancel speech. New recording waits for old playback to stop; a released hold gesture while waiting cancels the pending recording. No output echo capture. Serialize playback/replay and suppress stale status callbacks. A speech failure retains the readable answer and never triggers paste. Limit spoken text to 12,000 characters with a visible notice; retain the complete answer in the pill. Answers use a concise conversational style unless more detail was requested.

## Verification and Limits

Fixture tests exercise strict routing, intent instructions/context isolation, disabled defaults/config round trips, no raw JSON preview, speech fallback/cancellation and stale lifetimes. Native UI tests use hidden desktops and fake speech callbacks. No real microphone, user AI account, clipboard or audible playback during development. Verify managed and Native AOT builds/tests, then stage without restarting the user's app. Real Edge service/voice and physical playback acceptance remain manual unless an isolated fixed public synthesis check can run without account/audio side effects.

Alternatives considered: punctuation/keyword routing misclassifies polite edit requests; a second classifier call adds latency/cost; forcing every Ask AI response into a pill changes existing content insertion. The accepted optional semantic route retains both workflows.
