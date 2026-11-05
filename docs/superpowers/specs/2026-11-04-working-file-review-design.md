# Review multiple SharpVoice edits in the working file

Status: proposed for approval. The user chose working-file changes over a separate preview.

## Intent

Write successful code proposals into the active working document immediately. Allow further voice and typed code requests before the user decides to accept or revert the accumulated changes. Users can edit and test the resulting code with the normal editor tools.

## Approaches

- Separate preview: protects the source until acceptance, but cannot support editing and testing the actual working file as requested.
- Working-file changes with a tracked pending history: supports the requested workflow and targeted reversal. Selected by the user; tracking and conflict handling below are implementation proposals.
- Restoring a whole-file snapshot: simpler, but would discard unrelated manual edits. Do not use this approach for Revert All.

## Behavior

- Each successful proposal updates the working document as an undoable editor edit. Do not explicitly save or run code; normal VS Code save settings still apply.
- Highlight pending additions/changes in the source editor. Show deleted text using inline annotations where practical, and offer a cumulative diff for detailed review. No automatic switch to a separate diff.
- Subsequent requests capture fresh text and selection, including earlier pending changes. Only one generation/insertion runs at a time, but completed insertions do not block the next request.
- Keep pending history per document in the current extension window. Switching files or hiding the panel must not discard that history. Display the current file's pending count.
- Provide Accept All and Revert All in the panel and editor commands. Accept All clears review markers/history for the current document without rewriting or saving it. Revert All reverses tracked AI edits in reverse order as one undoable operation and clears resolved history. Individual-change controls are deferred; the user's explicit requirement is multiple changes before deciding.
- Cancel stops the active request; it does not revert previously inserted changes. Escape must not silently revert a batch.
- Preserve manual edits outside tracked AI ranges by updating offsets after document changes. If manual edits overlap a pending range, mark it conflicted; do not overwrite that region during reversal. Revert independent changes and retain conflicted changes with an explanation and an option to accept them. Later AI requests may intentionally overlap earlier AI changes; reverse dependency order must restore the original text when no manual conflict exists.
- Native Undo/Redo must update pending tracking or mark ambiguity as a conflict, never enable a destructive stale reversal.
- Pending history survives desktop bridge reconnects within this extension session. If an insertion succeeds but its receipt is lost, retain its local review history and report that the desktop receipt is unknown.
- On document closure or extension reload, edits remain in the working document according to VS Code's normal save/restore behavior; session review tracking is not durable. Do not claim Revert All remains available after reload. Explain this limit in extension documentation.

## Implementation boundaries

Separate short-lived generation sessions from local pending review history. Retain version/focus/result validation and the desktop apply handshake before insertion. Finish each desktop request after insertion so the next request can start; desktop Applied means inserted into the document, while local acceptance is a separate review decision. Update misleading approval/unchanged-document messages along this path.

Use stable VS Code extension APIs and the existing authenticated bridge. Keep current Windows/local/trusted-workspace restrictions. Do not send local pending histories over the bridge or alter context permissions. Preserve unrelated in-progress changes already present in the workspace. The builder can choose the tracking data structure, decoration rendering, command naming, and whether protocol changes are necessary; any protocol change must update both sides and the packaged extension together.

## Validation

Add focused tests for sequential insertions and replacements, overlapping AI edits, accept without additional writes, reversal preserving unrelated manual edits, conflict refusal, Undo/Redo, cancellation, duplicate results, stale results, bridge disconnect after insertion, file switching, and panel controls. Run extension build/unit tests and relevant desktop protocol tests if that contract changes. Package the extension using the existing repository workflow after successful verification; report any Windows editor checks unavailable in the current environment.
