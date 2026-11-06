# Usability and stability pass — November 5, 2026

Scope: existing native Settings and supporting dialogs, microphone monitoring, IDE request/review flows, cancellation, and the pending desktop fallback change. This is a local maintenance pass; it does not publish a release or implement the separate settings-card mockups.

## Corrections

- Settings action buttons respond consistently to Enter and Space. Hidden/disabled actions and held-key repeats cannot activate them. Existing specialized download, connection and help handlers remain in place.
- Shortcut captions work for both Hold and Toggle gestures. Connection help hides together with Test Connection when AI is disabled.
- Opening Updates from its notification selects General as well as showing its controls. Audio monitoring eligibility now agrees with the displayed page.
- Changing the Codex executable cancels its discovery, discards its old catalog and draft permissions, and requires discovery/permission selection for the new executable. Late results cannot appear under another executable's identity.
- Cancel invalidates pending code and panel Ask requests even while a file is opening or the bridge is connecting. Preparing a code request exposes a busy state and cancellation action.
- Clarification and shortcut validation recheck session identity, document/focus state and related context after asynchronous context collection. Canceled or changed requests cannot send stale context.
- An old ordinary validation completion cannot cancel a newer replacement request.
- Escape is available during regular panel Ask AI, as it is for hotkey Ask AI.
- Individual review controls follow the same busy/clarification rules as batch review. Conversation changes are disabled while work or code clarification is active; Copy Answer, Delete and Clear All are disabled when their required content is absent.

## Review coverage

The native review covered action dispatch, keyboard handling, tab changes, settings validation, parent/child Save scopes, microphone monitoring suspension, update actions and asynchronous connection discovery. The plugin review covered startup, cancellation, clarification, language restrictions, inline changes, accept/revert, duplicate messages, manual edits and disconnected handoffs. A separate reviewer checked the native changes and cancellation guards.

New regressions were run before each corresponding fix. Evidence logs are under `.build-cache/usability-*`; the final verification record is in `docs/verification.md`.

## Remaining limitations

- Native Settings and supporting dialogs still use fixed-height layouts. Their bottom actions can fall outside a small desktop work area (for example 1280×720). Responsive sizing/scrolling remains a concrete follow-up for the settings refinement work; keyboard improvements do not resolve this layout limitation.
- Automated checks use synthetic audio and isolated provider/editor fixtures. They do not certify physical microphone drivers, corporate proxies, every editor fork, every DPI/accessibility combination, or live provider availability.
- The existing ChatGPT OAuth configuration is unchanged. Its registration/migration question is separate from this pass.
