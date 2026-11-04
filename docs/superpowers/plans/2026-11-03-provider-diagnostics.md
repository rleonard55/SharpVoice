# Provider diagnostics implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Actionable provider failures with session-only sanitized reports and voluntary browser issue submission.
**Architecture:** Core owns a bounded typed journal/report. Refiner wraps complete operations with deadlines and typed errors. NativeDesktop uses the existing tools window for preview/copy/browser actions.
**Tech stack:** C# 14, .NET 10, Windows native UI, Native AOT; existing offline executable test harness.
**Spec:** docs/superpowers/specs/2026-11-03-provider-diagnostics-design.md (approved).

## Global constraints

Maximum 20 records, memory only; maximum report 16 KiB. Allowlisted fields, no arbitrary messages/content/credentials/identifiers. Explicit clipboard copy and fixed GitHub URL; no automatic uploads or report contents in URLs. Preserve TLS/proxy configuration and cancellation. Work in the existing checkout; commit/push verified source without a packaged release.

## Review focus

Nested refresh/catalog failures must be recorded once (task 2). Deadlines must cover streaming (task 2). Secrets in exceptions, headers, bodies and model IDs must never enter reports (tasks 1/2). Copy must use the displayed snapshot and Clear must discard it (task 3). Browser/clipboard failures must retain the preview (task 3).

## Task 1: Journal and operation boundary

Files: new Core/ProviderDiagnostics.cs, Refiner/ProviderOperation.cs, Tests/ProviderDiagnosticChecks.cs; modify test Program.cs.
Interfaces: ProviderDiagnostics.Shared, Snapshot(), Report(), Clear(); enums for operation/category; ProviderOperation.RunAsync<T>(operation, body, cancellationToken, timeout?); ProviderFailureException with a sanitized record.

- [x] Write and run failing Gemini TLS regression against existing behavior.
- [x] Implement typed journal and classification boundary; verify bounds, secret exclusion, nesting, deadline and caller cancellation.
- [x] Verify targeted checks with Windows test apphost.

## Task 2: Production integration

Files: GeminiApiClient.cs, UnifiedRefiner.cs, Auth/ChatGptAuthManager.cs, diagnostic tests. Consume task 1 interfaces; wrap complete token exchange/refresh, catalog and inference operations. Use fixed explanations and numeric status only.

- [x] Add failing HTTP category, malformed-response, interrupted-stream and nested attribution tests.
- [x] Integrate operation wrappers with deadlines through response reading and no duplicate records.
- [x] Run targeted and full offline checks.

## Task 3: Native UI and repository support

Files: NativeDesktop.Diagnostics.cs, settings/tools/connection handlers, GitHub issue template, docs/privacy.md, README/user guide, Windows smoke tests.
Consume task 1 journal. Snapshot all records oldest first on open; Copy uses preview snapshot; Clear empties journal and snapshot. Fixed issue URL and public-visibility notice.

- [x] Add failing native preview/copy/Clear regression.
- [x] Implement native UI with keyboard support and readable light/dark themes.
- [x] Add issue template, privacy and corporate troubleshooting documentation.
- [x] Strict AOT publisher exit verified (0), no warnings/errors.
- [ ] Commit/push and verify remote revision.
- [x] Full offline/native checks and independent privacy review.

## Execution ledger

Ruling: Execute inline in the current checkout under the user's Go ahead authorization, without another plan/worktree approval round. The approved scope and existing checkout establish authorization; no unrelated changes are present.

Task 1 complete: initial Gemini TLS regression failed before implementation and passes after classification. Bounded allowlisted report and privacy/category tests pass.
Task 2 complete: catalog attribution, nested deadlines, timeout/caller cancellation, Gemini body failures and token refresh tests pass. Preserved the existing IOException contract for interrupted streams after the full suite caught a regression.
Task 3 validation: native preview/copy/fixed URL/Clear/boundary-failure checks pass on an isolated desktop without touching the real clipboard or browser. Light/dark screenshots inspected; sharing notice made fully visible. Full offline suite: 195/195 passed.
Final review: independent read-only review identified HTML block-page categorization and lost provider-limit guidance; both reproduced RED, corrected, and verified GREEN. An older SSE fixture was updated to declare the real text/event-stream content type.
Final: minor (deferred): the latest explanation and report are sampled separately; a concurrent new failure can make the explanation newer than the report snapshot. Copy always matches the exact preview.
Privacy: changed-source Gitleaks 8.30.1 scan reports no leaks. Native candidate starts successfully with --release-info without audio/account access.
