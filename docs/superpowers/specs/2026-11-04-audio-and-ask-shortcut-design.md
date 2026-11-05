# Automatic Audio monitoring and IDE Ask shortcut

User approved removing Preview Applied Mic and automatically monitoring the saved microphone while Audio settings are visible, plus capturing the configured Ask AI shortcut in the shared VS Code/Antigravity extension.

Audio: replace manual button with status indicator; use existing recorder preview ownership so keep-ready capture is reused. Start only while settings/Audio are visible, app idle, not paused, and no answer playback/start/processing owns the microphone. Stop on tab/window exit and active work, resume when eligible; no automatic transcription or AI. Draft device selection remains unapplied until Apply Settings.

Ask: while a supported local editor is focused, authenticate/correlate Ask handoff just like Dictation, route capture/results to plugin Ask workspace, never source insertion. Keep Include Current File opt-in from the panel. Preserve hold/toggle readiness, stop/cancel, stale/focus/expiry guards. A nested Ask chord promotes pending/active code Dictation into plugin Ask without clipboard selection. Outside supported editors retain desktop Ask behavior. Missing plugin gives an actionable message and no fallback insertion. App and plugin update together.
