# Editor shortcuts implementation plan

> Use superpowers:executing-plans; implement in the existing checkout while preserving uncommitted work.

**Goal:** Exact Document/Comment/Uncomment shortcuts with deterministic initial-selection scope.
**Architecture:** Desktop classifies exact transcripts before generation, asks the extension to validate/retarget using captured initial selection, then emits either a provider-generated documentation result or a native editor action. Extension invokes native comments under the same handshake and review transaction as generated edits.
**Spec:** docs/superpowers/specs/2026-11-04-editor-shortcuts-design.md
**Tech:** C#, TypeScript, VS Code stable editor commands, protocol 5.

- [x] Tests first: exact shortcut classification, initial targeting, no-selection no-op, document full-file/selection, rejected model native actions, pending review.
- [x] Implement pure shortcut routers and retargeting; preserve ordinary selection refresh behavior.
- [x] Integrate native comment operations as recorded EditorReview transactions with version/focus validation and explicit add/remove commands.
- [x] Wire desktop routing and validation, update protocol/version, package metadata, tests and documentation.
- [x] Run full extension suite, isolated VS Code language/Undo/review fixtures and desktop build/checks; package and inspect VSIX.

Review focus: selected lines ending at next line column zero; selection changes during recording; no initial selection; unsupported comment languages; AI cannot request native actions; native command fails or loses receipt; CRLF/Unicode; whole-file size bounds.

Scope expanded by the user: Explain shows a read-only AnswerConversation response for initial selection/file; Format invokes native selection/document formatting with native Undo. Formatting stays outside AI-owned review to prevent ambiguous async manual edits from being deleted by Revert All.

Review fixes: Document clarification could retarget later selections; a failing regression now proves it remains frozen. Async Format could misclassify user typing as AI edits; reproduced with a deferred formatter and eliminated by keeping formatting in normal editor history. Final extension suite: 49 tests pass. Native VS Code fixture passes all five shortcuts and C#/Python/HTML/CSS comment syntax.

Final verification: managed desktop build and standalone Native AOT publish passed without warnings/errors; 199/199 desktop checks and authenticated native-shortcut bridge fixture passed. Standalone --release-info bootstraps without starting the UI. VSIX CRCs/compiled assets and embedded/distributable hashes match. Matching update is verified in artifacts/aot-update with update.ready written after copy/hash verification. User app/editor were not restarted or installed into.

Language-validity follow-up: screenshot reproduced native comments in strict JSON. Added deterministic rejection for Comment/Document in JSON/JSON Lines/CSV/TSV on both extension and desktop, strengthened provider instructions, and parse-validated full JSON/JSON Lines candidates before writes. Safe JSON Uncomment is planned locally and validated before insertion; string literals and unselected lines remain intact. JSONC stays supported. No general compiler/schema-validity guarantee is made for other languages. New regressions failed before fixes; all 60 extension tests and isolated native VS Code checks pass. Fresh code review found no important defects.
