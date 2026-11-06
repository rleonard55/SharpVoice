# Native Visual Refresh and Plugin Update Release

Approved scope: finish the reviewed Indigo design in the native UI, including the four-step setup wizard, and publish a new GitHub release with automatic updates for installed IDE plugins.

1. Apply common light/dark palette, compact branded header, colored navigation, restrained card borders and consistent enabled/disabled button treatments. Retain provider artwork and accessible native controls.
2. Finish native setup with matching header, step indicator, content cards, explicit microphone testing and isolated practice controls. Preserve navigation, cancellation and AI/settings handoff. Add Clear Text and guard unavailable microphones.
3. Carry reviewed context rules into Updates: Check Now is primary/default; installation requires an available/ready update and still downloads when needed. Keep signed-in account management and Title Case wording.
4. Verify managed behavior, native settings/setup screenshots in both themes, extension tests, packaged native app, installer lifecycle and asset hashes. Obtain a fresh final code review.
5. Commit the completed work, push/tag v0.1.11, and publish the verified GitHub release.

Ruling: continue in the shared working checkout, preserving and including the previously requested uncommitted fixes, because the user explicitly requested finishing this work and releasing it. No unrelated cleanup or user-profile installations.
Ruling: retain native controls and familiar window geometry rather than introducing a browser UI; visual consistency comes from shared design tokens, card grouping, hierarchy and icons. This preserves keyboard/accessibility behavior.

Progress: plugin maintenance already implemented and previously verified (213 checks); release must revalidate the final combined source.

Native settings/setup captures passed in both themes; all 135 extension checks passed. Setup Clear Text and unavailable-device guards are implemented. Update availability regression was observed failing before the fix, and the initial full native dialog suite passed after it.

Final review found one Important item: provider readiness must gate Test Connection, including direct commands. A regression was added for ChatGPT, Claude, Gemini API and Local AI readiness transitions and cancellation. The regression was observed failing before the fix; the final full native help/dialog suite passes, including active-test cancellation after readiness loss (`.build-cache/visual-readiness-green.log`).

Ruling: the release retains the native application's fixed settings/tool geometry and Windows DPI behavior. Verification covers the available desktop at its configured scale in both themes; it does not establish 125–200% or very small work-area support. Updates was reduced to a compact 720×670 dialog; setup is 700×700.

Final fresh source review found no Critical, Important or Minor issues. The review covered readiness/direct commands, setup navigation and unavailable-device guards, update installation, plugin maintenance/retry/shutdown and correlated hotkey fallback/cancellation. Expanded DPI/work-area support and live account/microphone acceptance retain the stated limits.

Resumed verification: managed build passed with zero warnings/errors; all 213 desktop checks and 135 plugin checks passed. All six Settings tabs and four setup steps were captured in both themes; representative final Settings/setup images were visually inspected. Bundled VSIX 0.3.4 CRC/manifest and JavaScript/media/docs match the tested files. An initial build collided with another compiler's output-file lock; after that compiler exited, the sequential retry passed.

Tasks 1–4 complete. Full native release build and isolated installer lifecycle passed. All seven asset checksum entries, portable CRC/native identity and exact embedded VSIX bytes passed; eight release files include the checksum manifest. Native SHA-256: `471d20f6d362878c2da0d7c754861c02a056c8f68788c77d343c633395d02fa1`. Evidence: `.build-cache/release-0.1.11-build.log`, `release-0.1.11-lifecycle.log`, `release-0.1.11-assets-verified.json`. Task 5 remains the approved commit/push/publication action, with uploaded SHA-256 verification required before making the release public.
