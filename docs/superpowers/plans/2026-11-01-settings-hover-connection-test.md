# Settings Hover and AI Connection Test Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline, with one final independent review.

**Goal:** Add obvious Settings tab hover feedback and an on-demand test of the currently selected AI provider/model.
**Architecture:** Extend existing native tab painting and mouse tracking. A cancellable Settings action invokes an isolated UnifiedRefiner with fixed public input and no speech/selection/insertion or preference changes. Result includes response model/time; errors have readable context help. Existing provider clients and OAuth remain unchanged.
**Tech Stack:** C#/.NET 10, native Win32, existing provider clients, Native AOT.
**Spec:** Current user request; implementation choices stated in commentary. Existing native UI/auth/provider constraints apply.

## Constraints
- Use selected draft provider/model; no Apply, actual user content, clipboard, microphone or HUD changes.
- Tiny real request only when user clicks Test; never auto-run on startup, hover, model refresh or sign-in.
- Bypass word cleanup threshold via Ask AI; independent refiner prevents model events changing dictation HUD.
- Local test requires already downloaded, hash-valid weights; never download 491 MB just to test.
- Single test at a time; Cancel Test, selection changes, auth, tab/close/Apply, dictation and disposal cancel/invalidate old results.
- Deadline; distinguish cancellation, timeout, provider failure and empty/incomplete response. No credentials or provider bodies displayed.
- Hover does not select tabs; mouse leave resets; native keyboard navigation and theme behavior remain.
- Leave a running app alone. Stage update after validation; copy portable only when unlocked.

## Tasks
- [x] RED native missing-Test control and hover behavior; service fixed-request/routing tests.
- [x] Implement native hover and cancellable Test Connection UI/service/app wiring.
- [x] Full managed suite, isolated native UI checks and dark/light preview inspection.
- [x] Final fresh independent source review; reproduce/fix material findings with regression checks.
- [x] Strict Native AOT builds/checks, docs, verified staged/portable executable and update marker.

## Ledger
- User provided concrete additions; routine native UI choices proceed within authorized scope without another approval round.
- No live paid/account request will be made by development checks; HTTP/CLI fixtures exercise the behavior. A user's Test click is the explicit request to use their account.
- No Git staging/commits: workspace is untracked under parent repository.

- Completed nonempty replies prove connectivity even when the model adds punctuation. A regression reproduced false failure (80/81) before removing strict echo matching. Test Enter handling also failed its native regression before a dedicated subclass/message-loop handler prevented fallthrough to Apply.

- Managed validation: 81/81 behavior checks and isolated native UI checks pass. Default/custom Settings round trips pass; light/dark connection/hover renders inspected. No real account request was made by development checks. Native CLI/provider integration still uses the existing independently covered UnifiedRefiner routes.

- Final independent review: no Important/Critical findings. Cancellation generations, shutdown task tracking, draft routing, keyboard handling and hover painting reviewed.
- Final: minor (deferred): Some provider errors still use dictation-oriented wording (for example “Nothing was inserted” or Apply guidance) in connection-test help. This does not change which draft selection is tested or save settings.

- Final verification: 81/81 managed and AOT checks; native hover/connection/help and default/custom Settings checks pass. Strict app/test publishing confirmed clean; first test wrapper termination was resolved by a successful confirmation publish. Stage and unlocked portable SHA-256 match `c1368451f21bd12d2e183bf9362a4b1a14f146eaeb7d6997ca7e506d8e74951e` (13,919,232 bytes). No instance was stopped or launched; update.ready created after verification.
