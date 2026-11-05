# Follow-up questions in the editor plugin

User scope: both Ask AI and code-edit clarification questions. Highlight the active question above the composer, provide optional model-generated reply buttons, and play a distinct sound once when a new live question arrives. Preserve typed and voice replies.

Use structured metadata rather than punctuation detection. Code results gain optional choices; plugin Ask AI opts into structured answer/clarify output. Plain desktop answer popups keep their existing format. Choices are at most four distinct nonblank strings of at most 200 characters. A clarification may have no choices. Code and ordinary answer results cannot carry choices.

A theme-aware question card remains outside scrolling history and uses an accessible live label. Render all provider strings as literal text. Clicking a choice submits its text through the existing reply route and never executes an action or accepts pending changes. Bind buttons to the exact question and reject stale/double clicks. Conversation changes, canceled operations, and provider changes must remove inactive questions. Workspace refreshes must preserve an active code clarification.

Implementation choice: extend existing bounded result/conversation contracts, not question-mark heuristics or a second inference call. A separate embedded two-note cue uses existing configurable audio feedback; replaying/restoring workspace state never plays it. Upgrade app/plugin protocol together.
