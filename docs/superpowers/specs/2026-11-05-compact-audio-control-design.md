# Compact audio input control

Status: user approved the combined-control layout for native implementation.

## Intent

Reduce the height and visual clutter of Audio Settings while keeping the live input level, noise gate and local live-transcription preference easy to find and adjust. Retain the selected Blue + Indigo design.

## Approaches

1. **Combined meter, selected by the user:** draw live signal and a draggable gate marker on the same scale, retain a small editable dB field, and replace the Live Transcription dropdown with a labeled toggle. This removes the separate threshold slider row and repeated gate readouts.
2. **Compact separate rows:** keep the existing separate meter/slider and tighten spacing. Lower implementation cost, but retains the repeated information and separate adjustment scale. Not selected.

## Proposed layout and behavior

- One compact Input card: Input level, current dBFS and above/below-gate status on the first line; combined meter/threshold beneath it; Noise gate numeric field and Live Transcript toggle sharing the bottom line.
- Muted below-gate signal, green above-gate signal, indigo gate handle, readable paused/waiting/invalid states in both themes.
- Drag the threshold or adjust it using keyboard arrows; the numeric field stays synchronized and accepts values in the existing −90 to 0 dB range.
- Live Transcript toggles the existing local draft preference. It does not hide the level meter or turn off microphone monitoring.
- Preserve Apply Settings behavior: numeric/marker edits preview the draft gate; the recording configuration changes only on Apply. Monitoring continues to use the applied microphone. No new mic-start or recording action.
- Keep microphone selection, Wake Phrases, Refresh Microphones and Mic Activation available. Use the space freed below the card for a taller Last Result area on Audio; other tabs retain their layout.
- Builder choice: implement in the existing native C#/Win32 UI, retaining native keyboard/accessibility semantics and bounded GDI resource use; no new UI framework or WebView.

## Preview

`artifacts/design-mockups/compact-audio/index.html` is an interactive design preview alongside the existing mockups. Input levels are simulated; the preview does not request microphone access, change app settings, or contain user audio. Compare dark/light themes and adjust the marker/field/toggle.

## Verification for implementation

Exercise marker drag, keyboard steps, typed values and invalid input; confirm exact field synchronization and Apply behavior. Verify checked/unchecked state persists, paused/waiting states, theme switching, native accessibility and on-demand help. Check native layout/meter renders and relevant settings/capture tests. Retain the existing unpublished ChatGPT Ask fix.
