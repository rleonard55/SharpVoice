# Claude backend and remaining acceptance implementation plan

> Execute inline with superpowers:executing-plans. No additional agents or commits.

**Goal:** Remove Groq and offer Claude subscription use through the official unmodified Claude Code executable; advance original-spec acceptance without claiming unperformed desktop tests.

**Architecture:** ChatGPT remains the default. Claude Code owns OAuth, refresh and credential storage. SharpVoice invokes the native CLI without a shell, sends speech/context over stdin, disables tools/MCP/hooks/project settings/session persistence, and requires a successful complete result before insertion. No Claude.ai token collection or imitation of Anthropic client identities. The optional CLI is an external dependency. Existing Groq profiles migrate to ChatGPT/auto while preserving other settings.

**Tech stack:** .NET 10 Native AOT, Process/JSON, existing native Settings controls and regression harness.

- [x] Regression checks for removed-provider validation/migration; port completion/error stream checks to ChatGPT rather than dropping coverage.
- [x] Remove Groq configuration choice, requests, environment keys and UI copy; migrate legacy profiles.
- [x] Add focused ClaudeCodeClient and owned-process runner: cancellation, bounded pipes, result validation, actual model metadata and safe native executable lookup. Fixture checks for flags/stdin, complete/error/truncated results and auth metadata.
- [x] Integrate backend routing, provider-specific one-at-a-time Settings account actions and Claude model choices. Preserve selected editing/Ask AI prompts.
- [x] Managed build/checks, native AOT app/test publish/checks, offscreen Settings checks; isolated subprocess check where possible without invoking inference.
- [x] Refresh capture/Whisper/app measurements; document remaining physical acceptance checks, supported Claude auth route and installation steps. Stage verified executable.

Reference: https://code.claude.com/docs/en/legal-and-compliance#authentication-and-credential-use ; https://code.claude.com/docs/en/headless ; https://code.claude.com/docs/en/cli-reference (checked 2026-11-01). Direct third-party Claude.ai login is not offered; the official binary owns sign-in. Do not run --bare for subscription use because it skips OAuth credentials.

Completion: 57/57 managed and AOT checks passed; default/custom native Settings and both themed snapshots passed. Official CLI update and existing subscription status passed. Managed/native production connection requests reported claude-haiku-5-5. Read-only review found no Critical/Important issues. Latest performance/acceptance limits and artifact hash are in verification.md; physical QA and performance guarantees remain explicitly open.
