# Exact editor shortcuts

User requirements are explicit: Document targets the initial highlighted code, or the entire file when no selection exists. Comment and Uncomment operate on the initial selection only; absent initial selection means no document action.

Recognize exact spoken or typed commands, case-insensitively, with surrounding whitespace and terminal speech punctuation ignored. Longer requests such as “document this function” retain ordinary generation behavior. These shortcuts apply only to the editor code workflow, not general Ask AI.

Use the existing provider for Document with a fixed instruction to add idiomatic language documentation and preserve behavior/unrelated code. Use native VS Code add/remove line-comment commands for Comment/Uncomment (never toggle), avoiding model-generated commenting. Preserve initial range even if selection changes during recording. Native commenting uses complete selected lines as VS Code does, remains undoable, and joins pending working-file review. No selection produces a visible no-op with no provider call or editor command.

Whole-file documentation explicitly requests document context and remains subject to the existing 32,000-character bound; reject oversize files rather than silently documenting only nearby code. Local commenting need not send the entire document to the provider. Native actions are emitted by the desktop shortcut router, never accepted from model output. Version the desktop/extension protocol together to prevent old clients misinterpreting native actions. Keep current source version/focus checks and apply handshake.

Implementation choices: protocol 5, extension 0.2.3; initial selection is captured before asynchronous editor startup. Normal code requests retain the current ability to select a target while speaking. Add focused routing, source-scope, no-op, language and pending-review tests, plus isolated native VS Code checks.

User expanded scope in chat to include Explain and Format. Explain sends the initial selection or entire file to AnswerConversation and publishes an answer without any apply handshake or source edit. Format invokes the configured native selection/document formatter and uses native Undo. Formatting is treated as an ordinary manual editor operation rather than AI-owned pending history, so async provider cancellation cannot make Revert All delete user typing. Existing pending AI ranges rebase or conflict around formatting as they do around other manual edits.
