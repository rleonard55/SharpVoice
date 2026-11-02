# Gemini Antigravity Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. Steps use checkbox tracking.

**Goal:** Add subscription-backed Gemini to all SharpVoice voice modes.
**Architecture:** Official native Antigravity CLI owns Google authentication; a dedicated client routes complete text requests through the existing refiner. Settings uses provider-specific account/model state.
**Tech Stack:** C# .NET 10 Native AOT, Win32 Settings, official Google CLI.
**Spec:** docs/superpowers/specs/2026-11-01-gemini-design.md

## Global Constraints
- Google owns browser sign-in and credentials; no paid API-key fallback.
- Flash is preferred when available; preserve explicit model IDs and provider separation.
- No tools, inherited hooks/MCP/plugins or user-file access for refinement.
- Keep six Settings tabs, General startup controls and existing tray actions.
- Do not change real saved credentials or interrupt the running user app.

## Review Focus
- Missing/old CLI must fail with setup guidance before inference.
- Failed/cancelled/incomplete output must never be pasted.
- Provider switching and late discovery must not overwrite another provider’s model/account state.
- Spoken prompts and selected text must stay out of command-line arguments and persistent local logs.
- Global CLI customization and alternative billing must not leak into this provider.

### Task 1: Verify the official CLI contract
- [x] Inspect official installer and native CLI help/version without authenticating.
- [x] Establish supported isolated settings, tools, credential ownership, status and model commands.
- [x] Record exact observed contract or blocking limitations before implementing an adapter.

### Task 2: Provider client and request routing
Files: new src/SharpVoice.Refiner/AntigravityClient.cs; UnifiedRefiner.cs; Core AppConfig.cs/RefinementAuthentication.cs; new tests/SharpVoice.Tests/AntigravityChecks.cs.
- [x] Add failing fixture checks for normal/selected/Ask mode routing, complete answers, failures and process isolation.
- [x] Implement verified native CLI invocation, bounded cancellation, economical model selection and exact result validation.
- [x] Run managed regression suite and confirm new checks pass.

### Task 3: Native Settings and application lifecycle
Files: NativeDesktop Settings/Models/account controls; SharpVoiceApp.cs; WindowsSettingsSmoke.cs.
- [x] Add native Settings checks for Gemini model/account routing and provider independence.
- [x] Implement backend choice, status/discovery, owned sign-in flow, busy guards and HUD model labeling.
- [x] Verify default/custom preferences, tab navigation and Apply; inspect light/dark snapshots.

### Task 4: Review and deliver
- [x] Read-only source review; resolve important findings.
- [x] Managed and strict Native AOT build/checks; update README and smoke/verification docs.
- [x] Stage verified executable and portable copy, then mark update.ready. Real Google sign-in remains user-owned.

## Ledger
- User approved Antigravity route; unrelated dictionary/snippet/history/threshold ideas remain proposals, not part of this implementation.
- No Git staging/commits: workspace is untracked under a parent Documents repository.

- Ruling: actual CLI owns account state and has no standalone login/status subcommands. Show Google account unconditionally for this backend; open Google's interactive UI and never claim sign-in merely because it closes.
- Ruling: use verified global text-only agent discovery; mutable skills/plugins map frontmatter was rejected by the native CLI. Minimal documented excludeDefaultComponents + tools:[] worked with a fixed non-private request. Existing agent modifications are refused, not overwritten.
- Ruling: CLI1.4.12 is the floor; self-updater advanced this already-installed CLI from1.4.5. The installer detected the existing binary; no PATH/alias changes were made.
- Ruling: Antigravity has no verified no-session-persistence flag. Unlike Claude, Google-owned conversation/history storage may retain dictation. Disclose in Settings and README rather than promising memory-only retention for this optional runtime. Global hooks/MCP/plugins are refused before request dispatch; API-key mode is refused.
- Evidence: initial optional-provider test failed64/65 (unknown backend); native fixed Flash request succeeded with the minimal custom agent, no selected/user dictation data.

- Native contract evidence: workspace and global agent probes with unsupported skills/plugins object frontmatter failed safely as unknown-agent; minimal documented global Markdown fields succeeded. Alternate config-root environment probing did not produce supported per-app CLI discovery, so it is not shipped. The separate probe agent was removed; only the exact SharpVoice text-only definition remains for real adapter use.
- Preflight ruling: actual profile has enabled metadata/skills-only plugins with disabled sidecars. Permit these non-executable contents; block hooks/MCP/agent/rule contributions, unknown manifest capabilities, enabled/unknown sidecar configurations and linked entries, including Windows case variants. Do not disable or rewrite user plugin settings.
- Final independent read-only review found no Important regressions after discovery/casing/sidecar fixes.

- Final:68/68 managed+AOT, native Settings default/custom, both actual fixed managed/AOT Google requests, strict builds and light/dark preview inspection passed. Stage/portable SHA25697a6a348e98a89311781080812f3377bc408e51d5da4b4cd242c2a6415b708d5.
