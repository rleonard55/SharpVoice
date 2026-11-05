# Working-file review implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task.

**Goal:** Insert AI changes in working files and accumulate them until Accept All or Revert All.
**Architecture:** A pure review model tracks disjoint pending ranges, merges dependent AI edits, rebases manual changes and refuses conflicted reversal. Controller sessions finish after insertion independently of local acceptance. Decorations and panel controls expose per-document review state.
**Tech Stack:** TypeScript, VS Code 1.95, Node tests, protocol 4.
**Spec:** docs/superpowers/specs/2026-11-04-working-file-review-design.md

## Global Constraints

Preserve existing workspace changes; work in place because these changes depend on the uncommitted editor hotkey implementation. Keep Windows/local/trusted restrictions. Never explicitly save or execute code. Validate versions/focus and handshake before insertion. Pending history is session-local, surviving bridge reconnects.

## Review Focus

Broad replacements must trim unchanged context. Deletion anchors must conflict safely with ambiguous manual edits. Duplicate results must insert once. Lost receipts must retain local history. Host snapshots must retain local controls. Undo/Redo must never enable a stale reversal.

### Task 1: Review model

Files: extensions/sharpvoice-vscode/src/review.ts, test/review.test.cjs.
Interface: PendingReview with record(before,after), update(changes,text), accept(), and planRevert() returning safe replacement edits and unresolved conflicts.
- [x] Add tests for sequential/overlapping changes, manual edits before/after/inside ranges, partial reversal, deletion anchors, Undo/Redo and unchanged context.
- [x] Run tests red; implement disjoint ranges with overlap merging; run unit suite green.

### Task 2: Controller and panel

Files: extension.ts, model.ts, panelModel.ts, panel.ts, media/panel.js, package.json, test/host.cjs and panel tests.
- [x] Add regression tests for automatic insertion, fresh subsequent requests, per-file actions, duplicate results and lost receipts.
- [x] Auto-insert validated results after handshake, retaining local pending state. Add source decorations and cumulative review.
- [x] Expose Accept All/Revert All in panel and editor commands; Cancel only stops active requests.
- [x] Adapt Windows host fixtures and run available validation.

### Task 3: Delivery

Files: extension README, desktop insertion messages, package metadata and bundled VSIX.
- [x] Document save/reload and conflicts; update misleading approval wording.
- [x] Run extension suite and relevant desktop checks, package, and request scoped review.
- [x] Report verification and limitations. Do not commit mixed user work.

Execution authorized by user: “go ahead”. Continue inline without another approval checkpoint.

Verification ledger: 35/35 extension tests pass; isolated native VS Code integration passes including CRLF/emoji reversal and Undo review restoration. Desktop build passes with zero warnings/errors. Final desktop suite/package checks recorded in docs/verification.md.

Review findings: fixed LF-to-CRLF history mismatch with a failing regression followed by a passing suite; fixed surrogate-pair trimming with a failing range test followed by passing native emoji reversal. No important findings deferred.

Implementation decisions: work in the existing checkout to preserve dependent uncommitted hotkey changes; undoing Revert All restores conflicted review markers, protecting ambiguous ownership. No commits or user-editor changes performed.
