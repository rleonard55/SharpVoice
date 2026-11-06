# Desktop voice fallback inside supported IDEs

User authorization: allow the normal desktop voice route in VS Code/Antigravity when a code editor is not open, instead of requiring the SharpVoice plugin to dictate into the app. Keep the unrelated settings-refinement proposal untouched. No release or authentication migration is authorized by this bug-fix request.

- [x] Trace the process-level hotkey intercept and plugin source capture; reproduce missing-plugin failure through the app and no-source failure through the plugin tests.
- [x] Leave disconnected-plugin hotkeys on the existing desktop voice path.
- [x] Add an explicit authenticated no-editor fallback response; accept only the matching pending id/client/mode before expiration, with the same window/process/focused control and no active/paused/auth operation. Bypass editor routing once, retaining the original gesture and normal desktop insertion/selection checks.
- [x] Keep generic code errors, missing readiness, canceled/released/expired requests and focus changes from becoming fallback pastes. Preserve connected source editing and direct panel Ask behavior.
- [x] Exercise actual VS Code no-source behavior plus existing code/Ask/Undo fixtures; run synthetic app handoff checks in Hold and Toggle.
- [x] Fix and verify review finding: provider-free code rejection after gate acceptance must reset the Toggle gesture so the next press can start.
- [x] Final managed/full-suite and native publication/bootstrap checks, package verification and local launcher staging.

Plugin package: 0.3.4; additive no-editor response retains protocol 9. The updated desktop and plugin are needed together for connected no-editor fallback. Desktop version remains local 0.1.10; no GitHub release.

Review: one important Toggle retry finding, otherwise no concrete issue in correlation/focus/cancellation/late-start handling. Browser/plugin tests and isolated app fixtures use no physical microphone/provider credentials. The user's OAuth question was answered separately: current direct OpenAI PKCE setup uses a Codex-style shared client configuration, rather than a registered SharpVoice Sign in with ChatGPT client. Authentication code is unchanged.

Completed: 209/209 desktop checks, 124/124 plugin checks, real isolated VS Code host checks and native Hold/code, Toggle/Antigravity-process and provider-free Toggle fixtures pass. The review regression first failed with the exact consumed-next-press error and passes after correction. Final managed build has zero warnings/errors; Native AOT publish/bootstrap and exact embedded-plugin checks pass. Local delivery manifest: `.build-cache/editor-fallback-delivery.json`.
