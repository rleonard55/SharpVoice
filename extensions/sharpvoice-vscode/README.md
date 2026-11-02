# SharpVoice for VS Code

Use the SharpVoice Activity Bar microphone, status-bar entry, or **SharpVoice: Open Conversation Panel** for a persistent voice/text workspace. **Dock Right** opens the native Move View picker: choose SharpVoice and Secondary Side Bar once. VS Code remembers the placement.

Choose Ask AI, Dictate Code or Edit Code. Speak starts recording; wait for the tone, speak, then Stop Recording. Questions stay in the panel with Reply by Voice and a text composer/Send fallback. Typed code requests skip microphone capture. Code changes always use a native diff and explicit Apply/Cancel, with Undo and no automatic save or execution.

Ask AI shares the desktop's general conversation unless Include Current File is explicitly enabled. Document attachments/code threads are scoped separately; fresh document context is captured and validated on each coding request. New/recent/delete/clear controls manage bounded in-memory conversations. Closing/hiding the view or switching applications cancels unfinished work but preserves completed exchanges until the desktop app exits. App restart clears the panel's transcript. No extension/Webview transcript persistence is used.

Desktop Live Transcription displays provisional local Whisper drafts in the pill and panel. Draft words can change; the final full recording is transcribed after Stop. Audio Settings can disable drafts. Drafts are never pasted or sent to the LLM.

Install/update through SharpVoice Settings → General → Plugins, then reload VS Code. Extension **0.2.0** requires desktop editor protocol **3**. Windows native local trusted workspaces only; WSL/remote and virtual documents are unsupported. One editor window owns the active desktop channel at a time. Context permission is configurable as Selection Only, Nearby Code (default, up to32,000 characters), or Current Document when within bounds. Explicit selections take priority; nearby/document targets let named-block edits work without highlighting everything.

The editor-title microphone/edit controls and Command Palette commands remain available. SharpVoice uses its selected provider/model; no provider credentials are stored in this extension.
