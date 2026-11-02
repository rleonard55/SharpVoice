# Pill Transparency, Dismissal and Position

Intent: More transparent recording pill, quicker dismissal after paste, mouse dragging without activating the original typing target, and location restored after restart.

Decisions: 85% opacity (217/255), 900 ms after successful insertion; retain 2500 ms for other outcomes. Native caption hit testing provides dragging on the pill with NOACTIVATE/TOOLWINDOW/TOPMOST. Mouse interaction replaces the previous click-through style. Pause dismissal during movement; resume after release. Record a nullable PillPosition in config, preserving legacy default placement and negative multi-monitor coordinates. Clamp to the nearest monitor work area on launch/display changes. Save only position on drag completion through a dedicated host callback; never reapply recording/provider settings or interrupt active work.

- [x] Observe native opacity/drag regression fail; add core placement/config round-trip tests and native move/save/dismissal checks.
- [x] Implement pure geometry/persistence, native drag lifecycle and dismissal policy, and isolated position-save callback in the host. Verify no focus activation or settings application during drag.
- [ ] Run native regressions and full offline suite, review, privacy scan, build/test a 0.1.3 patch retaining the bundled model, then publish through the existing update feed.

Validation uses an isolated desktop and synthetic messages/config files, without audio, credentials, real mouse input or changes to the running app. Physical pointer dragging remains part of the documented Windows manual smoke test. Earlier release assets remain unchanged.

Local verification: the old opacity failed the native regression before implementation. Final managed build reports zero warnings/errors; all 181 offline checks pass. Isolated native checks pass opacity, restored position, single save after drag, no full settings application, preserved typing focus/topmost, stale timer protection, idle-drag dismissal suspension, update-restart blocking and the shorter paste timeout. Independent read-only review found no actionable issue. Changed-source gitleaks scan: no leaks. Native AOT/package lifecycle verification is pending CI.
