# Provider diagnostics and voluntary issue reporting

Status: approved by the user on 2026-11-03. Approach selected by the user: local preview, copy report, open GitHub in the browser.

## Purpose

Make ChatGPT OAuth and Gemini API failures actionable on managed Windows computers, including networks with Palo Alto TLS inspection. An inspection CA being trusted does not establish why inference fails. Diagnostics must describe observable failures without assigning blame to the firewall or weakening TLS verification.

## Approaches

1. **Selected:** keep structured diagnostics locally, preview a sanitized report, explicitly copy it, and open the repository's new-issue page. The user pastes and submits through their browser. SharpVoice stores no GitHub credentials.
2. Direct GitHub API submission: fewer steps but requires GitHub authentication, credential lifecycle management, and additional consent/error handling. Deferred.

## Data and lifetime

Proposed implementation choices: retain at most 20 provider failure records in memory for the current app session; provide Clear. Do not persist diagnostic history automatically. Reports are bounded to 16 KiB. Copying is explicit and replaces the clipboard with the exact previewed report; the app does not read the prior clipboard or promise to erase clipboard history.

Use a versioned, allowlisted schema: application version, Windows version (no device name), provider enum, fixed operation/endpoint identifier, failure category, numeric HTTP status when present, stable runtime error code when available, and elapsed milliseconds. Omit exact timestamps and all stable user/device identifiers. Bound any enum-like values; custom model IDs and arbitrary strings are excluded.

Never include audio, transcripts, prompts, selected text, responses, credentials, account IDs, headers, response bodies, arbitrary exception messages or stack traces, file paths, user names, IP addresses, proxy URLs, internal domains, or certificate subjects/issuers/thumbprints. Do not collect first and attempt to remove secrets with regexes. Nested exceptions can be inspected for typed categories/codes but their text is never serialized.

## Error behavior

Classify observable DNS, connection, TLS-handshake, proxy-tunnel/authentication, HTTP rejection, timeout, and prematurely ended response failures. Distinguish caller cancellation from timeout. Preserve existing specific model, quota, and validation messages where safe. Unknown failures stay unknown; TLS-handshake failures must not be labeled untrusted certificates without evidence.

Instrument ChatGPT token exchange/refresh, model discovery, and inference, plus Gemini API inference. Cover failures before headers and during response reading. Record one failure per failed operation and keep elapsed time scoped to that operation. Exclude local-model and CLI backends from this initial reporting feature.

Display a fixed app-authored plain-language explanation with relevant next steps; never display arbitrary exception or provider-response text in diagnostic explanations. HTTP 407 means proxy authentication; 401 means provider authentication; 403 may be provider permissions or filtering and must not be asserted to be a bad key. A 200 HTML block page or malformed JSON/SSE is an invalid response, without recording its body. Never disable certificate verification or silently bypass the configured proxy.

Propagating an already-recorded refresh or catalog failure through inference must not produce a duplicate record; carry an internal operation marker. Apply operation deadlines through body/stream reading, including after response headers arrive. Explain generic resets and invalid responses without guessing their cause.

## User flow

Expose Diagnostics from AI & Models, including after a connection-test failure. Use a native owned window compatible with the existing Windows UI and Native AOT. Show the most recent supported provider failures, an exact read-only report preview, and Copy Report, Open GitHub Issue, Clear, and Close actions. Export all retained failures (maximum 20), oldest first, with their count. Keep keyboard navigation and readable light/dark styling.

State visibly: diagnostics stay in memory until cleared or exit; copying writes to the clipboard; opening GitHub contacts GitHub; reports posted to a public repository are public and linked to the user's GitHub account. Sanitized does not mean anonymous. Users can copy the same report to their IT team instead.

Open only the fixed `https://github.com/rleonard55/SharpVoice/issues/new` destination with a static template selector if needed. Never put report contents in URL query parameters. Opening a page does not upload the report or create an issue. Do not submit issues automatically, reuse ChatGPT/Gemini credentials, or attach existing logs. Browser-launch/clipboard failures must be shown without losing the preview.

The issue template requests the sanitized report and optional user-authored reproduction steps, explains public visibility, and warns against adding confidential information. Update README and user guide with report fields, lifetime, sharing steps, and corporate-network troubleshooting. Include a privacy document linked from the template and diagnostics UI/help.

## Verification

Use synthetic HTTP handlers and streams to exercise DNS/TLS/proxy failures, HTTP 401/403/407/429/5xx, timeout versus cancellation, malformed responses, and interrupted streams. Verify stage attribution and one-record-per-operation behavior.

Adversarial fixtures place synthetic secrets, private text, internal hostnames, usernames, and certificate details in exceptions, headers, bodies, and custom IDs. Assert none appear in exported reports or generated URLs. Check report/history bounds and Clear. Confirm preview equals copied report, no background upload, and no issue body in browser URLs. Never use real provider credentials or company traffic for these tests.

Run the repository's full offline test suite and relevant Windows native UI checks; validate Native AOT compatibility with the existing build process. Report any unavailable Windows verification explicitly. Review the changes for privacy before source publication.

## Delivery

After approval, implement and test app changes, issue template, and documentation; commit and push verified source to the existing GitHub repository. No real diagnostic issue will be posted. A new packaged release is outside this change unless requested. Do not restart the user's app or change saved provider credentials/settings.
