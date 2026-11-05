# Provider diagnostic privacy

SharpVoice keeps up to 20 recent ChatGPT and Gemini API operation failures in memory. Clear in **AI & Models → Diagnostics**, or exit the app, to discard them. No diagnostic file, device identifier, background analytics service or automatic diagnostic upload is created by this feature. This policy describes provider diagnostics; existing optional dictation history and provider/CLI retention settings are separate.

Reports use an allowlist: report schema, numeric app and Windows version, provider, fixed operation and public endpoint identifier, failure category, numeric HTTP status/runtime error code when available, and elapsed milliseconds. Records are listed oldest first; exports stay below 16 KiB. They omit exact timestamps and custom model IDs.

Reports exclude audio, transcripts, selected text, prompts, AI responses, keys, tokens, account identifiers, raw exception messages and stack traces, request/response headers and bodies, file paths, user/device names, IP addresses, proxy URLs, corporate hostnames, and certificate identities. Unknown response text is never copied into diagnostics. TLS errors identify a failed handshake, not a proven certificate trust problem.

## Preview, copy and sharing

Opening Diagnostics takes a snapshot. New failures appear when you reopen the window. **Copy Report** explicitly replaces the clipboard with the exact displayed report. Clipboard history, synchronization tools and other applications may retain copied text; Clear removes the in-app records and preview, not clipboard history or already shared copies.

**Open GitHub Issue** opens the fixed SharpVoice issue form in your browser. It does not copy or upload the report and does not put report data in the URL. Paste the report yourself, review any details you add, and submit on GitHub if you want to share it. SharpVoice does not request or store GitHub credentials. The same report can be sent privately to your IT team instead.

Sanitized does not mean anonymous. A public issue is public and associated with the GitHub account submitting it. Opening GitHub or Privacy Details contacts GitHub, whose own privacy policy applies, including collection of connection/account metadata. Do not add confidential details or raw logs to a public issue. There is no promise that deleting an issue retracts copies others have made.

## Corporate networks and TLS inspection

Successful sign-in does not establish inference connectivity. SharpVoice exchanges ChatGPT tokens at `auth.openai.com/oauth/token`, discovers models at `chatgpt.com/backend-api/codex/models`, streams inference from `chatgpt.com/backend-api/codex/responses`, and calls Gemini at `generativelanguage.googleapis.com`.

Use **Test Connection** to send a fixed public probe without microphone, selection or clipboard content. Remote probes consume a small amount of provider allowance. Ask IT to correlate the operation/category with proxy, URL-filtering and TLS-decryption logs. Trusted inspection certificates do not rule out TLS negotiation problems, response rewriting, connection resets, or streaming timeouts. A 403 can reflect provider permissions or filtering; a 407 indicates proxy authentication. These diagnostics do not bypass the proxy or disable certificate checks.

No real credentials or company traffic are needed to run the offline diagnostic regression tests.


## IDE review recovery

The IDE plugin stores pending source text, original change baselines, file identities and conflict markers in local workspace extension storage, up to 2 MiB combined. This enables explicit Resume Review after an editor reload. It does not include conversation transcripts or audio and is separate from optional encrypted dictation history. Resume does not overwrite source; changed files are protected as conflicts. Discard Recovery removes inactive saved checkpoints; Accept All Files clears active and saved review history. The IDE and operating system govern the storage location and protection.


## IDE Ask context

Ask AI in the IDE automatically sends the active filename, extension, language and highlighted text to the configured AI provider. With no selection it sends file details only. Include Current File explicitly adds bounded surrounding/file content using the plugin context setting. Contextual Ask conversations are scoped to the file; with no active file, Ask uses the general desktop conversation. Ask answers remain read-only.
