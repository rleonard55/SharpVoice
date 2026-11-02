# Connected Ask AI Implementation Plan

> Execute inline with superpowers:executing-plans and TDD; fresh final review. User approved implementation with mandatory branded popup/diffs. No further design approval round.

**Goal:** Optional tool-connected spoken/typed questions and reviewed external actions.
**Architecture:** App-owned tool loop; existing text-only ChatGPT OAuth inference produces strictly validated steps. Codex app-server is an explicit MCP gateway, never an autonomous inference turn. Only approved selected tool calls leave the app. This enforces permissions independently of inherited app/tool approval defaults.
**Tech Stack:** .NET10 Native AOT, native Win32, Codex stdio JSON-RPC.
**Spec:** docs/superpowers/specs/2026-11-01-connected-ask-design.md

## Global Constraints
Off by default; existing dictation/editor flows retained. No paid API backend/global Codex config edits/credential copying. No live mail reads/sends or user profile mutation in tests. Windows local Codex initially; disclose WSL/profile differences. Build/restore sequential; hidden native fixtures only. No Git mutations in this untracked repository.

## Review Focus
Unselected/unknown calls; inherited permissive Codex policies; voice/stale/double approvals; closed popup/settings changes; process interruption after external write; model prose claiming success; bounded untrusted catalogs/results; source and editor data isolation.

## Tasks
- [x] Core contracts/policy: tool annotations, supported creation adapters, schema validation, complete readable creation diffs, one-shot bound approval. RED then GREEN with malicious/unknown/changed payloads.
- [x] Bounded cancellable Codex RPC and gateway: initialize, ephemeral gateway thread, paginated effective MCP catalog, explicit calls only, deny unknown server requests, no turn/start or shell/file APIs. Fake transport/real isolated protocol probe where possible.
- [x] Refiner-owned bounded tool loop and in-memory conversation: text-only strict step envelopes, selected tools only, trusted read annotations, mandatory preview for supported writes, receipts/unknown outcomes, cancellation. Fake provider/gateway tests.
- [x] Native themed Connected Ask popup (activity, before/after panes, exact action button, sources, typed/voice follow-up) and off-by-default settings with runtime discovery and per-tool allowlist. Hidden desktop fixtures.
- [x] App routing/speech/lifecycle: connected Ask path excludes editor selections/history/paste; popup cancellation and generation guards; original source retained for voice follow-up.
- [x] Full managed/Node/native UI/AOT checks, fresh review/fixes, documentation and guarded staged/portable app update.

## Rulings
- App-server used as tool broker rather than invoking its autonomous agent: stronger enforceable tool boundary; planner uses SharpVoice's existing ChatGPT OAuth text-only inference. No turn/start means no builtin shell/file/agent tool can execute. User did not require a particular agent-loop location.
- Start with verified new-message/draft/event creation adapters. Existing-record update/delete and unknown write schemas remain visibly unavailable until authoritative before-state adapters exist; never fabricate diffs or degrade to voice/text-only approval. This follows the design's fail-closed requirement. All permitted writes get complete creation diffs and explicit per-call confirmation.
- Runtime capability is discovered from callable MCP catalog, not mere app directory installation. Some hosted integrations may not expose callable tool schemas to the chosen local runtime; show this accurately, never infer availability from this chat.
