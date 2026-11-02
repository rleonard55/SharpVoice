# Code target and clarification repair

User reports unnecessary highlight requests and highlights ignored on follow-up. Root causes traced: prompt explicitly forbids dictation edits outside empty insertion range, and extension clarify restores old range while desktop never accepts a refreshed context.

Authorized correction: use already-allowed context as a bounded fixed preview target when no code is highlighted, preserving original cursor; explicit selections retain priority. Selection Only remains private and restrictive. Use updated unchanged-document selection on follow-up; retain original instruction; preview/Apply/Undo and version guards remain. No connected-assistant changes in this repair.

Execute inline with TDD and final fresh review. No Git mutations/user app/editor/account/microphone changes. Build/restore sequential; test profiles isolated. Do not edit the ServiceNow example code itself.

- [x] RED: automatic document target, context-sharing bounds and refreshed clarification tests.
- [x] Implement target metadata, bounded auto-target, current selection refresh, desktop retarget validation and prompt correction; protocol2/VSIX0.1.2.
- [x] Managed/Node/native-host/AOT verification, fresh review and fix findings.
- [x] Package plugin/app, document and stage guarded review artifacts.

Evidence: Node RED4/6; desktop RED110/111 on discarded selection; real hidden-desktop VS Code RED on missing refreshed context. Additional host RED reproduces highlights made during recording being omitted from generation. Version2 parser RED6/7 caught an old reader constant before correction. Fresh review identified a >32k single-line context collapse, reproduced7/8 then corrected to retain bounded raw character edges.

Ruling: user may highlight while already recording, so refresh deliberate nonempty selections again at pre-generation validation. Desktop validates same document/version and applies updated context before reading it for generation. A unique per-generation nonce prevents late validation replies from authorizing a newer round. Empty cursor moves during recording retain original insertion position. All source changes still cancel; no live user editor/account/microphone used.

Managed build zero warnings/errors; **111/111** suite, 8 Node groups, hidden native UI and real isolated VS Code host fixtures passed. Host proves no-highlight refactor preview/Apply/Undo, fresh selection during recording, refreshed voice follow-up target, source-change cancellation and readiness timeout. Fresh reviewer rechecked long-line and pre-generation refresh/nonce changes and found no further blockers. Actual live-model intent/hardware remains manual acceptance. VSIX source/output/embedded bytes verified, SHA-256 `50a59faee6ca5995dae3c659991ac0ec284904492c78c953fb0b34757c8358f9`. AOT publication running sequentially.

Native AOT test publication exited0 without warnings/errors; **111/111** checks, isolated native UI and VS Code host workflows all passed. Production plugin installer/reinstall passed against the independent VS Code binary with a temporary test-only profile. No user editor/profile/app/mic/account changed. Final app publication running alone after test processes exited.

App Native AOT publication exited0 without warnings/errors. Staged/guarded portable executable SHA-256 `80415aa645157c053470750a1bec3167ebc282d031ff67c526305b4155e7b34f`; matching PDB and update.ready prepared. User-facing docs/spec amendment updated. No user app/editor was started/stopped or plugin installed in their profile.
