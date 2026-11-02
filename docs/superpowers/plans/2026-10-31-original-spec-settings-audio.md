# Original spec: settings and audio controls

**Goal:** Complete the original spec's settings/audio-control milestone using the existing native Windows architecture.

**Architecture:** Keep Native AOT, source-generated JSON, direct WASAPI and Win32 controls. Split settings presentation out of NativeDesktop into a partial file. Add stable endpoint IDs to configuration and enumerate active recording endpoints. Applying a different microphone replaces the recorder only while idle; failures remain visible and can be retried through Settings.

**Spec:** `Data/Spec.txt`, amended by the user's later requests; `docs/spec-completion.md` tracks the whole specification.

**Constraints:** Preserve tray-only startup, Ctrl+Alt, tiny.en, OAuth/auto, embedded MasterPrompt, −65 dB and clipboard defaults. Preserve existing numeric hotkeys/config files. No audio saved by calibration. No new framework dependency. No automatic live provider requests or new API credentials for these controls.

## Tasks

- [x] Add configuration for microphone endpoint, custom chord and per-mode instructions; test backward compatibility, validation and prompt application.
- [x] Enumerate microphone endpoint IDs/names and open selected devices through WASAPI; native-check default and explicit selection without retaining audio.
- [x] Build Audio, AI & models, Prompts, Hotkeys and Insertion tabs; retain preview/Apply. Include gate slider, live input meter, editable AI model suggestions and per-mode instructions. Native-test navigation and unchanged/edited Apply.
- [x] Add interactive chord recorder and generic modifier/key state handling; test key order, repeats, injected events, release and Escape cancellation.
- [x] Wire idle-safe microphone replacement and tray quick model switching; preserve settings and retry capture when unavailable.
- [x] Build, run behavioral and native checks, inspect rendered settings, publish AOT update, and update requirement evidence.

## Added user request

Forms and the tray menu follow OS app light/dark mode, with improved spacing, typography, rounded grouping and native control/accessibility semantics. Runtime notifications update open forms. Theme palettes are supplied through a provider so native rendering tests can verify both themes without changing the user's Windows settings.

## Verification ledger

- Configuration/prompt contracts initially failed for missing properties/overload; implemented and included in 36/36 managed and AOT checks.
- Custom-chord parsing initially rejected Ctrl+Shift+Space; implemented generic state/capture and passed existing modifier/repeat/injected-event cases plus new capture/release tests.
- Native settings test initially failed because five tabs were absent; default/custom Apply, prompt navigation, gate edits and unavailable-device preservation now pass under managed and AOT.
- Native selected-device test initially failed for missing catalog/constructor APIs; actual enumeration and capture now pass under both runtimes without retained audio.
- Final review: fixed primary-first key suppression — matching release regression RED→GREEN; suite 36/36.
- Final review: fixed stale microphone readiness during model preparation — recorder availability is authoritative; native startup/disposal/invalid-endpoint readiness checks pass under both runtimes, and preparation checks current availability before reporting Ready.
- Final review: fixed owner-drawn menu accessibility — documented MSAA label allocation/lifetime test RED→GREEN; native model entries carry checked flags; suite 36/36. Interactive Narrator remains documented in the smoke test.
- Managed solution build: zero warnings/errors. Native AOT app/tests publish successful; one pre-existing upstream Whisper IL3000 warning.
- Dark/light offscreen renders reviewed; native live theme-change checks pass under managed and AOT. Physical key capture, live device unplug, tray submenu interaction, DPI and Narrator are interactive checks.

## Review focus

Disconnected saved microphones must remain selected and produce a recoverable error; a refresh must not silently choose another microphone. Switching tabs must preserve unsaved edits. Switching prompt modes must preserve unsaved prompts. Recorded modifier keys must not get stuck or trigger dictation while recording the shortcut. Apply and tray changes must reject active dictation and leave the running configuration consistent after failures.

## Execution

Work inline in the existing workspace; there is no isolated repository here (the parent Documents folder owns .git). Do not stage or commit unrelated user files. Run meaningful tests for settings persistence, chord state and native COM/control behavior. Record remaining specification gaps rather than claiming whole-spec completion.
