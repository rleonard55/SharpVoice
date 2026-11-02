# Start on login implementation plan

> Execute inline with superpowers:executing-plans.

**Goal:** Optional per-user Windows sign-in startup, off by default, configurable in the themed Settings form.

**Architecture:** Persist StartOnLogin in generated configuration. Add an Off/On row to the Insertion tab without expanding the tab layout. Applying writes only SharpVoice's HKCU Run entry, using a quoted current apphost executable and --startup. Roll back that entry if configuration saving fails. Never enable startup merely by installing or launching the update. --startup preserves tray-only launch and quietly exits if an instance already exists. Windows Startup Apps can independently disable registered startup apps.

**Tech stack:** Native Win32 Settings, Microsoft.Win32.Registry, existing .NET10/AOT projects.

- [x] Add regression checks for default/legacy configuration, native Settings round-trip, command quoting/validation and isolated-registry enable/disable/save-failure rollback.
- [x] Implement platform startup registration, generated configuration flag, Settings choice, save integration and quiet duplicate startup invocation.
- [x] Managed build/checks and isolated registry smoke; strict Native AOT publish/checks and themed Insertion snapshots.
- [x] Document opt-in use and portable location behavior; stage verified executable without changing the user's actual startup setting.

Reference: https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys (checked 2026-11-01). Registry command length must be at most 260 characters; reject unsupported host/path before mutation.

Verified: 59/59 managed and AOT checks passed, default/custom native Settings checks passed, both themes visually inspected, strict publishes warning-free. Read-only review found no Critical/Important issues. Actual Windows sign-in remains manual; real startup registration was not enabled during testing. Artifact hash is in verification.md.
