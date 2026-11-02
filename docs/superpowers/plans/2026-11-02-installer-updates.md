# Installer and GitHub Updates Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement and verify each task.

**Goal:** Build the first Windows installer and portable release with independently configurable automatic GitHub updates.

**Architecture:** Pinned Velopack handles installation and verified update packages. A testable coordinator owns scheduling/download/restart policy; SharpVoice owns UI-thread idle checks and orderly shutdown.

**Tech Stack:** .NET 10 Native AOT, Win32 UI, Velopack/vpk 1.2.161, PowerShell and GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-02-installer-updates-design.md`

## Global Constraints

- Windows x64 per-user install; preserve `.sharpvoice` preferences/credentials/models. No publisher token in clients.
- Automatic Updates defaults On; separate update action defaults Install When Idle. Notify/download never restart automatically. Off prevents scheduled checks and pending automatic installation.
- No interrupted voice, editor, AI, authentication or speech work. No automatic restart with visible settings/review windows; require 60 seconds idle.
- Fixed `rleonard55/SharpVoice` stable public feed; private/unpublished availability is a normal nonfatal condition.
- Do not stop the running development app, change repository visibility, publish the release, or expose local build paths/symbols.

## Tasks

- [x] 1. Add failing offline tests for independent preference persistence/validation, automatic/manual policy, cancellation and idle restart gating. Implement core policy and injected update coordinator.
- [x] 2. Integrate Velopack bootstrap, fixed GitHub source, shutdown/restart lifecycle and startup registration ownership. Verify AOT compatibility and lifecycle hooks without microphone/account access.
- [x] 3. Add Updates under General with separate controls, status/progress and manual actions. Verify both themes and saving controls on an isolated desktop.
- [x] 4. Add pinned packaging/tool configuration, sanitized native release script, checksum/package audits, third-party notices and draft-release workflow.
- [ ] 5. Build 0.1.0 installer/portable/feed, run full checks and isolated real lifecycle/update tests, review changes and repeat privacy scans. Commit/push verified source and prepare a private draft release if possible. Document artifacts and any remaining clean-machine/signing limitations.


Implementation and local verification: 175/175 offline checks; complete isolated Settings regression in both themes; actual random-identity Native AOT install, corrupt update rejection, upgrade/restart, downgrade prevention and uninstall; actual portable metadata; sanitized native packaging and asset string scans all passed. Final source push, GitHub workflow and private draft verification are the remaining publication steps.

Ruling: Disable both native debug generation and SDK symbol copying — the SDK copied a cached old PDB even after debug generation was disabled. Keep the package whitelist and path scans as independent guards.

Ruling: Wake-prefix speech, recognition drain and preparation count as activity before the further 60-second idle interval — review found they were not represented by the normal Recording phase.

Ruling: Preserve authentication refresh ownership through full disposal before replacement — the restart call now occurs outside the desktop/auth/HTTP lifetime scope. No credentials are refreshed as part of packaging tests.
