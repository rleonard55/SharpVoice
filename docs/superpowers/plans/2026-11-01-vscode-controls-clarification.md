# VS Code Controls and Clarification Repair

User requests: clicking Answer by Voice produced no tone; add onscreen start/stop controls.
Execution: inline in the existing untracked workspace, no Git mutations or redundant approval. No live app/editor profile/mic/account access. Stage app and VSIX; user restarts/installs.

- [x] Reproduce clarification/ready-state gaps using a hidden VS Code fixture and fake transport. Existing coverage only tested insertion/refactor/cancel; it did not cover answering a question. Trace current focus gate and unconditional recording label.
- [x] Restore and revalidate original editor on explicit follow-up, send clarify preserving context; distinguish arming/ready, show Stop only after capture acknowledgement, bound readiness wait and report errors. App emits recording only in real capture-ready callback. Keep typed clarification and cancellation/limits intact.
- [x] Add editor-title Dictate/Edit/Stop/Cancel/Answer/Apply icons and persistent idle status mic with stateful Stop/Answer/Apply. Autoactivate on startup without connecting/opening mic. Version VSIX0.1.1, retain protocol1.
- [x] Native hidden-desktop regressions for voice and typed continuation, focus restoration, readiness acknowledgement, stop/cancel, stale document/round limits and visible control contributions; full managed/Node/native/AOT verification, package installation in isolated profile.
- [x] Docs, verified hashes, guarded portable copy, VSIX artifact and ready marker. Live mic/no-tone root cannot be proven without user's hardware; report synthetic validation accurately.

Ledger: production already dispatches clarify to BeginEditorRecording, but extension gates it on transient active-editor identity and labels Recording before any capture acknowledgement. Explicit user follow-up permits returning to the unchanged source; it must still reject stale documents/lost window focus. Add tests before changes.

- RED native fixture: previous extension reported Recording instead of Arming immediately after a voice follow-up. The capture dispatch already existed; actual hardware no-tone failure was not reproduced. Restore/recheck source on explicit answer, show truthful readiness state, and surface a bounded no-readiness error rather than silently claiming capture.
- GREEN: 101/101 managed checks, TypeScript/4 Node groups and native voice + typed follow-up/source focus/readiness/stop plus idle control checks passed. New timeout/late-ready coverage added.
- Single focused final review identified P2 concurrent clarify submissions during async source restoration. A simultaneous-double-answer regression is being run before the fix; claim an intermediate state before awaiting to prevent a second frame/round.
- User-approved onscreen design: native editor-title icons (Dictate/Edit/Stop/Cancel/Answer/Apply) and persistent idle status microphone; no palette required. VSIX0.1.1 autoloads its lightweight UI, but never connects/opens mic until an explicit command.
- Review fix RED: two concurrent Answer calls sent two clarify frames (native fixture reported 2 != 1). Claiming Answering before awaiting source restoration prevents the duplicate, preserves one round, and avoids the desktop's second-call cancellation. No second review pass.

- Final verification: zero-warning/error managed and strict app/test AOT publications; 101/101 managed and AOT checks; TypeScript/4 Node groups; seven hidden native editor groups including follow-up single-flight/readiness timeout and idle control; native Settings/help/HUD/default/custom checks. VSIX0.1.1 archive/runtime/installation validated in isolated profile; docs updated; guarded delivery and hash marker completed. No real app/profile/mic/accounts/clipboard changed. User hardware no-tone remains a physical acceptance check.
