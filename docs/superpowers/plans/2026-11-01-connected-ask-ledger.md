# Connected Ask AI execution ledger

Implementation followed the approved design inline. No Git mutations/worktrees were made: SharpVoice is untracked inside the parent Documents repository. No live mailbox/calendar calls, user profile changes, microphone recording, app/editor launch or account changes were used for verification.

- Core diffs and approval fixtures were observed RED, then GREEN. Gateway and app-owned loop fixtures passed. The real process transport denies runtime approval requests and cancels only its owned child.
- Empty/canceled follow-up recording was observed RED in `.build-cache/connected-voice-red-ui.log`; ready-state voice controls now recover without closing the conversation.
- Native popup first failed with its unimplemented stub; implemented activity, complete diff, explicit one-shot action, typed/voice follow-up, readout, cancellation and OS themes passed hidden-desktop fixtures.
- App routes connected Ask before selection validation, writing preparation, history or insertion. Runtime/permission/settings/auth changes retire context; closing cancels work; shutdown unhooks connected callbacks before disposing the coordinator.
- The installed Windows Codex app-server was verified using a fresh isolated CODEX_HOME and a synthetic local MCP server: effective catalog and explicit tool result passed, without an inference turn or live account.
- A known creation tool carrying a contradictory read-only hint was observed RED in `.build-cache/connected-hint-red.log`; known creation semantics now take precedence and always require the review path. Header line breaks and nested implicit defaults are rejected.
- Sending account was verified by a selected same-server read-only profile tool and rechecked after approval. A missing identity test was observed RED, then GREEN. Changed identities stop before the write.

## Rulings I made

1. Use app-server as an explicit tool gateway, not an autonomous agent. SharpVoice's existing ChatGPT OAuth inference proposes strict text-only steps; only allowlisted RPC/tool calls execute. Cost: no built-in shell/file/agent capability or automatic parity with every Codex environment.
2. Initially support complete new message/draft/event creation schemas. Existing-record edits/deletions, opaque/default-dependent payloads, nonempty attachments and unknown schema/account formats fail closed. Cost: some otherwise available provider actions remain unusable until adapters exist.
3. Discover actual callable schemas, not app directory listings; support native Windows Codex first. Cost: hosted-chat/WSL/profile-specific integrations may not be available.
4. After a proposed write, report authoritative declined/error/verified-receipt outcomes rather than model-written completion prose. Unknown receipts stop without retry. Cost: mixed requests may receive a narrower action report rather than an unrestricted generated summary.

## Fresh final review

Fresh-context read-only reviewer: `connected_ask_final_review`. No Critical issues established. All five Important findings received regression fixtures observed RED before their fixes:

- Missing calendar destination/time zone, consequential schema defaults and hidden property names: `.build-cache/connected-review-all-red.log`.
- Model false-success reporting: same log. Final loop independently tracks outcomes and requires creation receipt identifiers; no-tool answer claims cannot report a completed action.
- Cancel leaving follow-up disabled and LF-only message bodies collapsing: `.build-cache/connected-review-all-red-ui.log`.
- Fixes passed managed120/120, targeted native UI, complete native UI, Native AOT120/120 and complete AOT UI before the additional bounded-rendering/persistence fixtures.
- Receipt-backed follow-up was observed RED in `.build-cache/connected-followup-red.log`; the conversation now retains actual retrieved-context eligibility across requests and does not repeat a read/write merely to answer from its existing receipt. Execution status explicitly applies to the current request.
- A subsequent untrusted-prefix bound fixture was observed RED in `.build-cache/connected-bounds-red.log`; bounded rendering and uncertain outcomes after unreadable write receipts were added. Final runs are recorded in docs/verification.md.

Final: minor (deferred): Sources-menu provenance — links currently come from activity text, including user/tool content; they are convenience links, not independently verified citations. Documented in docs/connected-ask.md.

A complete native UI run once failed the pre-existing hover-deactivation pixel assertion before Connected checks. An unchanged rerun and the AOT UI suite passed. An automatic permission-review timeout on the targeted hidden UI invocation was retried once successfully. One Native AOT run also hit the existing Edge-helper temporary-directory cleanup lock; earlier runs passed and the final isolated rerun is recorded below. The npm WSL shim failed; Windows Node type checking and all8 extension groups passed.

## Evidence

- Managed final build/tests: `.build-cache/connected-delivery-managed-*`.
- Final Native AOT test build/checks: `.build-cache/connected-delivery-aot-*`.
- UI evidence: `.build-cache/connected-review-green-ui*`, `.build-cache/connected-reviewed-full-ui*`, `.build-cache/connected-aot-ui*`.
- Real protocol: `.build-cache/connected-real-protocol.log`.
- Extension: `.build-cache/connected-extension-build.log`, `.build-cache/connected-extension-tests.log`.
- Live mail/calendar acceptance and physical voice/microphone behavior remain manual; synthetic success does not prove live provider compatibility.

Final: all123 Native AOT checks, complete hidden UI and8 Node groups passed. App/test Native AOT publication exited0; staged/portable SHA-256 b688dc64e2ac4deaad034a71b2d93247e1eb07d87abf5e46dc5c85519d0050f1. Existing-record/attachment/WSL compatibility limits and the Sources provenance minor remain disclosed; no live-provider acceptance is claimed.

Final managed rebuild exited0 with no warnings/errors; `.build-cache/connected-delivery-managed-tests.log` reports123/123. Final Native AOT UI also passed empty/canceled voice retry. No integration branch action was applicable to the untracked parent-repository layout; delivery remains in the authorized workspace and staged launcher update.
