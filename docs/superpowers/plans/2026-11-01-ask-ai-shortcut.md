# Ask AI shortcut implementation

> **For agentic workers:** Use superpowers:executing-plans for assigned work. Do not stage or commit.

**Goal:** Add hold-to-request Ask AI mode, default Ctrl+Alt+Shift, returning only requested answer/content for insertion.

**Architecture:** Preserve the capture/transcription/provider/insertion pipeline. Add explicit request intent separate from dictation/selected-text editing. The current backend/model and normal insertion safeguards apply. Highlighted text is optional request context in Ask AI, rather than spoken editing instructions. Backend none explains the AI requirement and inserts nothing; blank speech still skips inference. Answer-only system instructions exclude preamble, unsolicited summary, signoff and wrapping but allow requested content/format.

**Shortcuts:** Keep instant Ctrl+Alt recording. If the Ask chord becomes active during that recording, promote the same recording to Ask without a second start cue or restart. Shift-first starts Ask directly. Once a recording ends, do not reactivate a nested shortcut until the gesture's keys are released. Support independent/custom shortcuts and balanced suppression of non-modifier key pairs; ignore injected events. Persist both shortcuts and reject equivalent duplicates. Older configuration files receive the new default.

**UI:** Separate Dictation and Ask AI selectors/record buttons in Hotkeys, clear HUD Ask state while recording/thinking/inserting, existing theme/icons preserved.

- [x] Implement/test dual-shortcut routing and backward-compatible configuration.
- [x] Implement/test explicit Ask AI intent, isolated answer-only prompt, selected context and empty/local-only guards.
- [x] Integrate Settings, HUD and coordinator without double recording/cues/insertion.
- [x] Run managed/native behavior and Settings checks; strict AOT publish app/tests; update docs and stage launcher update.
