# Connected Ask AI

Connected Ask AI is optional and starts **Off**. It reuses callable tools exposed by your **Windows Codex profile** and SharpVoice's ChatGPT account sign-in. It does not use a paid Platform API key, copy connector credentials, or modify Codex settings.

1. Open **Settings → General → Connected Ask AI**.
2. Leave the executable blank for automatic detection, or select an absolute path to a Windows `codex.exe`. Codex CLI on PATH and the bundled VS Code Codex executable are detected.
3. Choose **ChatGPT Sign In** if required. This account supplies the answer model; your Codex profile supplies tools. Your ordinary cleanup provider can remain None.
4. Choose **Discover / Test Connection**. Discovery lists metadata, not email content. The connection area identifies the runtime and Codex profile directory.
5. Available tools are shown by default; **Show Unavailable** reveals unsupported capabilities on demand. Choose individual tools with **Allow / Remove Tool**, or choose **Allow All Available Tools** to permit the currently discovered supported collection. Enable Connected Ask and **Save Changes**. The draft supports up to 4,096 distinct tool permissions, matching discovery; tools added later need permission separately. Unknown writes remain unavailable and supported writes still require review.
6. Use **Ctrl+Alt+Shift** to ask a question. The branded window shows activity, actual retrieved tool results, source links where available, and the answer. Ask typed follow-ups or choose **Speak Follow-Up**, then **Stop Recording** when finished. **Read Answer** uses existing Edge/Windows speech; press it again to stop.

Connected Ask takes precedence over Smart Ask for this shortcut when enabled. Ordinary dictation and explicit VS Code voice-code commands keep their existing behavior. Connected requests do not capture or send editor selections/documents and do not paste answers or add tool results to dictation history. Context exists in memory while the window is open. Closing it or applying settings clears that conversation.

## Reviewing changes

Each supported external write pauses for a complete creation diff. The review shows the verified account, exact integration, all supplied destination/recipient fields, subject, body, attachments or event fields. The account is read again after confirmation; a changed identity stops the action. Activate the specific **Send Email**, **Create Draft** or **Create Event** button to approve that one exact payload. Typing/saying “yes,” recording shortcuts, timeouts and closing the window cannot approve it.

Only selected tools with read-only annotations may run automatically. Initial creation adapters recognize `gmail_send_email`, `gmail_create_draft`, `google_calendar_create_event` and `gcal_create_event`. Writes also require the same integration's selected, read-only `gmail_get_profile`, `google_calendar_get_profile` or `gcal_get_profile`, accepting an empty argument object and returning one identifiable email account. Messages require explicit email addresses and plain-text subject/body. Nonempty attachments, opaque payloads, unsupported fields and omitted schema defaults are blocked. Calendar creation requires an explicit calendar ID, summary, and start/end objects with dateTime UTC offsets plus timeZone. Tool naming and schemas vary; an unsupported schema/account response blocks the change rather than guessing. Existing-record edits/deletions are unavailable until a trustworthy before-state adapter exists.

Cancel interrupts pending work. It cannot undo an action already accepted by a service. If a write loses its receipt, the window reports **Outcome Unknown**; check the service before repeating it. Tool receipts remain visible independently of the model's summary. After a proposed write, SharpVoice reports authoritative action outcomes rather than model-written completion claims. Missing creation IDs become Outcome Unknown. A model answer alone cannot mark a change successful. The Sources menu currently lists convenience links found in activity, including user/tool text; these are not independently verified citations.

## Compatibility and privacy

This first version uses native Windows Codex, not WSL profiles. Tools installed only in this chat, hosted environments or another Codex profile may be unavailable. A directory listing alone does not make a tool callable. If discovery returns none, configure connections in the selected Windows Codex profile; SharpVoice cannot export this chat's tools or credentials.

The answer model receives the question, selected tool descriptions/schemas and bounded retrieved results. Large allowed collections use local metadata search to expose a small relevant set of complete schemas per planning step, rather than including the entire collection in every prompt. Metadata search uses only permitted, supported tools and does not retrieve account content. Services receive actual approved tool arguments. Responses and activity are kept in memory and cleared when the window closes; SharpVoice does not persist raw tool arguments or mail bodies to its own logs/history. Codex and the connected services retain their own policies. No shell, local file editing or autonomous Codex inference turn is started: SharpVoice validates text-only model proposals and invokes explicit selected MCP calls.

The gateway has been tested against the installed Windows Codex app-server with an isolated profile and a synthetic local MCP server. Live mailbox retrieval/sending and real calendar actions have not been tested.
