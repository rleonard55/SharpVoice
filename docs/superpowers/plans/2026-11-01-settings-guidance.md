# Settings Guidance Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline with one final independent review.

**Goal:** Give all six Settings tabs clear choices and concise inline decision help.
**Architecture:** UI-only label/value mapping keeps persisted IDs and provider routing unchanged. Fixed layout gains short contextual explanations; model IDs typed by users remain supported.
**Tech Stack:** Existing native Win32 C# Settings, .NET 10 Native AOT.
**Spec:** User request for inline guidance and Title Case choices with short parenthesized descriptions. This is a specified presentation change; no new service or workflow.

## Global Constraints
- Preserve all saved values, default selections, option ordering, authentication guards and prompt drafts.
- Use friendly Title Case choice names, retaining brand names/acronyms; use short parenthetical qualifiers.
- Explain tradeoffs without promising accuracy/speed or making subscription/billing claims beyond existing behavior.
- Keep all help visible in light/dark layouts and six tabs accessible without scrolling.
- No real user preferences, startup registration, running app or OAuth changes.

## Review Focus
- Every label resolves back to its exact config ID on Apply and provider/account/model refresh.
- Prompt navigation must use stable keys and preserve unsaved drafts.
- Explicit editable AI model IDs survive catalog changes.
- Unknown/unavailable devices and saved models remain represented truthfully.
- Help text fits the existing card; keyboard use and focus remain intact.

### Task 1: Choices and compatibility
- [x] Extend native Settings checks for friendly labels and all speech choices round-tripping, then observe failure.
- [x] Add centralized label/value mapping; use it in all routing, Apply and prompt paths.
- [x] Standardize AI model labels while preserving explicit IDs.

### Task 2: Inline guidance and delivery
- [x] Explain microphone/gate, model tradeoffs/providers/cutoff, modes/prompts, hotkeys, insertion timing/limits and startup behavior.
- [x] Build; run regression and native Settings checks; inspect all tabs and both themes.
- [x] Independent source review and fix material regressions.
- [x] Strict native app/test publish; native checks; docs and verified staged/portable update.

## Ledger
- Work in existing workspace; no Git staging or commits (untracked project under parent repository).
- User specified the presentation; native controls and static inline help remain appropriate. No external links are required for choices whose decision can be explained in place.

- Native regression RED: raw tiny.en displayed instead of Tiny (English, Fastest). Account-routing smoke checks then caught a remaining direct display-text read in the main sign-in handler; mapped to the stable ID. Final managed 71/71 and native Settings default/custom pass, including every speech ID and explicit provider models. All six tabs inspected in both themes; incomplete concurrent dark Hotkeys render was rerun individually and passed visual inspection.

- Final independent review found no Critical/Important issues. Its minor copy suggestion was incorporated: local AI speed depends on the PC rather than a categorical cloud comparison. This is a reversible wording correction; visual/compiled native checks cover it without a copy-mirroring test.

- Delivery: final managed/native suites 71/71 each, Settings default/custom pass in both, strict native app/test and managed builds clean. README/smoke/verification updated. Staged and portable executables verified identical; update.ready created last.
