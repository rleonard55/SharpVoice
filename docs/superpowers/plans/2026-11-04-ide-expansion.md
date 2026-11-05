# IDE Workflow Expansion Implementation Plan

> **For agentic workers:** Use executing-plans and dispatching-parallel-agents for independent modules; preserve the existing dirty working tree.

**Goal:** Deliver all ten approved IDE features through the shared extension.
**Architecture:** Native editor tools and bounded project context are independent modules. Controller integrates them with per-hunk review, local recovery and a sequential multi-file editing queue. Protocol carries optional context only; model never chooses commands to execute.
**Tech Stack:** TypeScript, VS Code APIs, C# bridge/refiner, Node tests, Windows native fixtures.
**Spec:** docs/superpowers/specs/2026-11-04-ide-expansion-design.md

## Global Constraints
- Preserve manual edits, version/focus guards, native Undo and explicit review.
- Related sources: at most 4, at most 8000 characters each, 32000 total. Guidance: at most 8000 characters; current user/language rules win.
- Local review checkpoints: 2 MiB combined, no conversation/audio, explicit resume/discard, no automatic source writes.
- Task execution only via explicit native task selection; report real exit result.

## Review Focus
- Stale CodeLens/panel identities and reloading a changed file cannot revert unrelated code.
- Coordinated edits must stop on cancellation/conflict and disclose partial progress.
- Exact custom phrases cannot shadow native commands or execute a shell.
- Related context and instructions cannot bypass target/language constraints.
- Task completion, diagnostic counts and test outcomes must reflect actual editor events.

### Task 1: Native IDE tools (features 2, 4, 6, 9)
- [x] Add module/tests: exact voice/text commands, symbol/diagnostic pickers, read-only explanations, generation callback and task exit reporting.
- [x] Verify native API behavior and cancellation/freshness.

### Task 2: Project context (features 3 context, 7, 10)
- [x] Add module/tests for selected-file context, ancestor guidance, custom shortcut resolution and bounded literal data.
- [x] Integrate optional protocol context, validation and provider serialization.

### Task 3: Review and recovery (features 1, 5, 8)
- [x] Test and implement per-change identity, accept/revert, serialization/recovery and stale/conflict protection.
- [x] Integrate CodeLens, review list, explanation/jump actions and workspace review controls.

### Task 4: Controller integration and coordinated edits
- [x] Register commands/settings and exact typed/spoken routes; connect all modules and show context/verification status.
- [x] Implement bounded sequential existing-file edits with fresh related source and honest partial completion.
- [x] Add controller and native host regressions.

### Task 5: Delivery
- [x] Independent code review; address material findings with regressions.
- [x] Run extension/desktop/native fixtures, package shared extension and Native AOT app, verify and stage paired update.
- [x] Update user docs and activation instructions.

Completed in the existing authorized workspace without committing or disturbing prior uncommitted work. Independent modules and review evidence live in `.build-cache/ide-expansion`. Final validation: 115 extension tests, 205 desktop checks, isolated native VS Code fixture, clean managed build and Native AOT publication, exact embedded VSIX and staged dependency hashes. See delivery.json and docs/verification.md.
