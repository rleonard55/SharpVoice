# Prototype scope — 2026-10-31

The user selected a full end-to-end prototype after reviewing the options of a native foundation first or a working dictation path first. This is the exploratory prototype path: implement the complete workflow so native integration, recognition quality and remote-refinement behavior can be evaluated before production hardening.

The proposed design uses seven C# .NET 10 modules at the workspace root. Native Win32 handles the tray, HUD, settings, hook and insertion. WASAPI uses direct COM vtables and Windows shared-mode conversion rather than a managed audio dependency. Whisper.net provides local inference, with verified external model files and native CPU runtime DLLs. Optional ChatGPT subscription OAuth and Groq provide refinement. The current default is ChatGPT OAuth/auto; choose none for local transcription only.

The application captures the foreground window/control when recording begins, serializes one dictation at a time, counts 800 audio frames after release, transcribes, optionally refines, validates the original target and then inserts. Failed refinement retains the raw transcript. Completed results are retained in memory for explicit copying. Pausing stops the audio client; exiting cancels pending work and releases native resources.

Initial builder decisions (later superseded by user requests): F8 was the initial hold key, F1–F12 were configurable, recordings stop at 120 seconds, settings use native controls, clipboard mode replaces clipboard content, and the loopback listener binds IPv4 port 1455 using TCP to avoid URL ACL setup. Those are prototype decisions, not additional user requirements. ChatGPT model defaults come from the supplied spec and remain editable; its backend is experimental.

Validation includes compilation, portable behavioral checks, a local Whisper fixture when available, and a documented Windows smoke test. AOT publish requires a Windows toolchain. This iteration makes no measured claims about the spec's memory, latency or packaging targets. Production-only backend breadth, richer UI/device controls and performance optimization remain outside this prototype.

## Continued original-spec milestone

The current app uses Ctrl+Alt, tiny.en, ChatGPT OAuth/auto, the embedded user master prompt, a −65 dB gate, clipboard insertion and tray-only startup. The continued settings/audio milestone adds native tabs, selected microphone capture/reconnection, gate slider and input meter, per-mode prompts, recorded custom shortcuts, editable AI-model suggestions, a compact tray menu and OS light/dark presentation. `spec-completion.md` remains the requirement checklist; packaging, rendering, backends and performance targets are not complete.

## Provider scope update — 2026-11-01

Groq is removed at user request. ChatGPT remains default; Claude subscription support uses the official unmodified native Claude Code executable, which owns OAuth. The optional bridge adds a separately installed CLI dependency and returns completed responses. Earlier prototype scope descriptions above are historical; current acceptance is tracked in spec-completion.md.

## Writing tools update — 2026-11-01

Current fresh defaults are Tiny Q5 English speech, AI cleanup off, hold-to-talk shortcuts, On Hotkey microphone activation, an eight-word cleanup threshold and memory-only text history. Explicit saved preferences survive upgrades. Optional ChatGPT, local AI, Claude Code, Antigravity and Google AI Studio API-key providers remain available.

Personal dictionary, exact voice snippets, executable app profiles, the newest 50 text dictations with explicit model retry/copy, and toggle recording with optional silence auto-stop extend the prototype. Rules live in General; gestures live in Hotkeys. Saving history across launches is opt-in and encrypted for the current Windows user. Retry does not automatically reinsert text. The HUD now shares its title row with smaller, muted, right-aligned model/status text and gives the preview two lower lines. Live hardware, accessibility, high-DPI and performance acceptance remain separate manual work.
