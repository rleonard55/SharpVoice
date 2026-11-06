# SharpVoice visual review — 6 November 2026

Status: visual exploration for review. No production styling changes or release.

Open `artifacts/design-mockups/visual-refresh/index.html` in a browser. Links were also added to the existing design gallery and compact-audio preview.

## Deliverables

- 69 native fixture captures: six Settings tabs in both themes; main tool dialogs; provider selection/dropdown; help; connection/download/update/diagnostic states; answer and Connected Ask review; all four setup steps; tray/account menus; completed HUD; wake diagnostic utility.
- Existing application/tray/provider ICO assets rendered at 16, 32 and 64 pixels, the IDE microphone SVG, and the 27 distinct native action/tab/menu glyphs at 16 and 24 pixels.
- Two generated concept boards: Light Indigo settings/Wake/HUD, and app identity/icons/status/pop-ups.
- A clickable light/dark prototype retaining six tabs and ten supporting dialog sketches; light/dark screenshots and a smaller-window popup capture.
- A proposed microphone app-mark SVG, displayed at actual 128/64/32/24/16 CSS-pixel sizes.
- `manifest.json` records capture names, themes, grouping, provenance and coverage limits.

## Visual direction

Use a cool near-white page, white cards, ink text and restrained Indigo actions. Replace the large brand banner with a compact app identity/header. Keep the familiar tabs and approved combined input meter. Place Refresh next to its selector, and keep device status with the device. Group microphone readiness and wake phrases without implying that readiness and wake listening are the same state.

Use a consistent icon weight, sentence-case labels, aligned fields and predictable action placement. Preserve branded provider icons. Use text and symbols alongside teal success, amber questions, and recording/error colors. A simpler microphone silhouette should remain recognizable at tray sizes; production artwork still needs separate pixel fitting and high-contrast variants.

Dialogs should fit their tasks. Short account/key/update tasks get compact windows; dictionary, history and permission managers retain enough room for readable lists and content. On small work areas, scroll content and keep primary actions reachable. Parent Apply and child Save/Cancel scopes remain separate; this is not an auto-save proposal.

## Interpretation and limits

Native captures use synthetic fixture states; they are actual native renders, not live user account or microphone activity. Wake diagnostics follows the current OS theme. External browser sign-in, OS-owned file pickers and every possible error message are outside this main-screen inventory.

Generated boards are mood/layout references. Their text, apparent icon sizes, sample statuses and meter geometry are not exact implementation requirements. The HTML prototype is more precise about labels and draft states. Supporting dialog sketches are not exhaustive field specifications. Moving Last Result to Audio only and changing dialog layouts remain proposals.

Before implementing, resolve exact field coverage and layouts for each form, long labels/device names, busy/error/disabled/focus states, 125–200% scaling, high contrast and the smallest supported work area. The compact header is a proposed evolution of the previously selected Indigo banner.

## Verification

- Current native capture modes completed successfully, including menu capture and all setup steps. Test harness build: zero warnings/errors.
- Real Edge: 69 prototype checks passed across tabs, both themes, dialog open/close, Apply draft state and gate synchronization.
- Real Edge: all 117 gallery images loaded before the two final popup references were added; subsequent local reference validation covered those additions.
- Inspected native Settings/Wake/menu captures and rendered light/dark prototype images.
- `git diff --check` passed.

Screenshot harness changes are limited to an opt-in menu capture mode and additional setup-step outputs. Existing production work from the prior usability pass remains untouched.

## Visual revision: color and tab icons

Owner feedback: retain the overall design, add subtle color and restore recognizable icons. The prototype now uses six inline SVG tab icons (speaker, AI chip, document/pencil, keyboard, insertion arrow and gear), with teal/violet/amber/blue/rose/slate section accents. Soft card tints, a restrained header glow and slight button/app-mark depth address the flatness. A microphone icon identifies the microphone card. Icons accompany text labels, remain decorative to assistive technology, and use lighter accent variants in dark mode. Standalone SVGs are in `concepts/icons/`. This remains a prototype-only revision.

Revision verification: 71 browser checks passed, including all six SVG tab icons, normal-width tab fit, light/dark tab and dialog navigation, and draft controls. Light/dark Audio screenshots and an AI & models screenshot were refreshed.

## Review Revision: Updates, Provider Icons and Title Case

Owner feedback supersedes the earlier always-enabled install proposal: Check Now is the initial focused and accent action; Download and Install And Restart remain disabled until a check reports an available update. The preview includes current/available/failed scenarios. A completed simulated download leaves Install And Restart enabled. Save Changes remains a separate, secondary policy action.

Restored the existing OpenAI, Gemini and Claude icon assets in the provider selector and its choices, including ChatGPT, Gemini API, Claude Code and Antigravity; local/off choices use neutral symbols. The prototype listbox supports arrow keys, Home/End, Enter/Space, Escape, Tab and type-ahead. Selecting a provider retains its model/account behavior and marks the settings draft.

Interface labels, buttons, headings and option labels now use Title Case, including Auto and Test Connection. Explanatory prose, typed text, code and model identifiers retain their casing. This prototype preference replaces earlier sentence-case guidance for the eventual implementation. Production code remains unchanged.

Revision verification: 88 browser checks passed across both themes, update gating and download completion, provider icons and keyboard selection, draft tracking, and Title Case labels. Updated Updates/provider screenshots were visually inspected.

## Review Revision: Contextual Actions

The prototype now keeps separate sample readiness states for each provider. Signed-in ChatGPT/Claude accounts show Re-Authenticate and Sign Out; signed-out accounts show Sign In; expired sessions require re-authentication before testing. Antigravity account actions identify their external account manager. Gemini API offers Add/Manage API Key and Remove Key, while local AI offers Download Model only until a model is available. None hides provider actions. Test Connection and model selection require readiness; Refresh Models applies only to ChatGPT and requires sign-in. Account controls simulate states only and never touch real credentials. Changing a provider still marks the parent settings draft, while changing a design scenario does not.

Context conventions: hide actions that do not apply; disable actions with unmet prerequisites and provide an explanation. Copy Last Text is disabled for an empty result. Dictionary/snippet entries require both fields, then Add To Draft before Save. Empty profiles and undiscovered Connected Ask tools cannot be saved. Dialog Save remains disabled until preferences change and disables again when the changes are reverted. Operational update checks remain independent of policy drafts.

Verification: 117 browser checks passed, including account transitions, API-key/local-model prerequisites, hidden None actions, dictionary/snippet entry validity, profile requirements, and dialog draft/revert behavior. Production behavior remains unchanged.

## Setup Wizard and Plugin Update Follow-Through

`setup.html` carries the same Indigo header, section colors, SVG icons, compact cards, Title Case controls, light/dark themes and contextual disabled actions through four steps: Microphone, Shortcuts, AI Setup and Try Dictation. Microphone testing stays explicit; leaving the step stops its simulated test. AI is optional and hands off to the existing Settings preview. Practice never inserts into another application, disables Finish during recording, and clears recording state on navigation. Skip and Finish have distinct completion states. The native wizard restyling remains a design proposal.

Automatic plugin updating is implemented in app source for the next release. A background coordinator checks existing installations when the packaged app or bundled VSIX version changes, uses the official editor CLI and bundled package, skips absent/current/newer installations, serializes against manual installation, verifies installed versions and retries failures next launch. Successful checks are version-stamped atomically. Restart waits for maintenance; shutdown cancels and drains it. Plugins shows status and recovery guidance. The installed editor must be reloaded to activate an updated extension; SharpVoice does not close editors or install into absent plugin installations.

Setup browser checks: 28 passed; existing Settings checks: 117 passed. Light microphone and dark practice screenshots were inspected.

Backend verification: app and test harness Release builds passed with zero warnings/errors. The focused `--plugin-auto-update-check` passed eligibility, installation verification, failure isolation, manual/automatic serialization, version-stamp retries and cancellation checks using simulated editors. No installed user extensions were changed.

Full regression verification: 213/213 desktop checks passed when launched through `SharpVoice.Tests.exe`. An earlier DLL-hosted invocation passed 208/213; its five failures depended on `Environment.ProcessPath` pointing to the test executable and were resolved by using the correct executable entry point. No product code changes were needed for those failures.
