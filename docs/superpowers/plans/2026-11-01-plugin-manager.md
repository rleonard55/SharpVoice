# Plugin Manager Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline. Steps use checkbox syntax.

**Goal:** Install and update bundled SharpVoice editor plugins from Settings.
**Architecture:** Platform service owns discovery, package and bounded CLI operations. Existing themed tools UI owns display, cancellation and explicit install commands. App wires them together.
**Tech Stack:** Native Win32, .NET 10 Native AOT, existing dependency-free VSIX.
**Spec:** docs/superpowers/specs/2026-11-01-plugin-manager-design.md

## Global Constraints
- No real editor/profile/microphone changes during development.
- No shell invocation, automatic installs, provider changes or downgrade of newer plugins.
- Antigravity editor integration is experimental until host-tested; its account CLI is not the editor.

## Review Focus
- Missing and versioned editor layouts, paths containing spaces.
- Query/install failures must never report success.
- Closed/reopened UI must ignore stale asynchronous completions.
- Newer installed packages must not be silently downgraded.
- Installation must preserve ordinary dictation and mandatory code previews.

## Tasks
- [x] Add failing fixture tests for detection, package identity, exact CLI arguments, versions, failures and cancellation.
- [x] Implement `EditorPlugins` in Platform.Windows with bundled VSIX, detected known editor paths and injectable process boundary.
- [x] Add General → Plugins manager with per-editor status, explicit install/update/reinstall, Refresh and cancellation on close.
- [x] Wire service, recognize Antigravity editor processes and preserve preview protocol.
- [x] Verify managed tests, native UI, isolated VSIX install and AOT; obtain final read-only review.
- [x] Update README/verification and stage app without starting it or touching live editors.

## Rulings and Evidence
- User requested implementation; proceed within that scope without an extra permission round for reversible local changes.
- Existing repository is wholly untracked under a parent repository; no Git mutations.
- RED: both new checks failed because versioned editor discovery was absent; `.build-cache/plugins-red-checks.log`.
- GREEN: final managed build clean, 103/103 checks; isolated native UI passes including Plugins lifecycle/themes; production installer/reinstall verified in a workspace-only VS Code profile.
- Read-only final review found no blockers; fixed its minor shutdown cancellation-source disposal note and rebuilt/retested.
- Automatic approval review blocked an isolated-profile test using the installed Antigravity IDE binary, because starting the user's installed editor is restricted. No test was run and its real profile/extensions were not changed; compatibility stays experimental pending the user's decision.
- Final AOT app/test publications passed; 103/103 AOT checks, native hidden-desktop UI and production installer/reinstall in another isolated VS Code profile passed. Four Node test groups/type checking and deterministic packaging passed. Bundled VSIX equals the distributable archive.
- Staged/portable executable: `9c502ea5c725f9764331b99937fd5f9d546bef995ad665d71291ee6eaa6c0ca4`. Guarded portable copy succeeded without starting any app. Main running installation is left for the launcher to update on user exit/restart.
