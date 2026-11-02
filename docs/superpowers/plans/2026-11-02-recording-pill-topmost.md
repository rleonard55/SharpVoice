# Recording Pill Topmost Fix

Intent: Raise the recording/status pill above other apps on each explicit show without stealing typing focus or changing its placement, click-through behavior or dismissal.

Evidence: HUD creation sets TOPMOST, but both SetActive(true) and SetStatus(showHud:true) only call ShowWindow. Add one shared show helper that reasserts HWND_TOPMOST with NOACTIVATE, NOMOVE, NOSIZE and SHOWWINDOW. Do not add a recurring z-order timer or activate/focus the HUD.

- [x] Add a native isolated-desktop regression: retain a synthetic text field's focus, lower the HUD, raise a competing topmost window, then show through recording/status/repeat paths. Assert visibility, z-order, styles, geometry, focus and normal dismissal. Observe the old implementation fail.
- [x] Route both show paths through the helper. Run the native regression, existing HUD rendering check and full offline suite. Add the native check to release validation.
- [ ] Build/check a model-included patch installer (0.1.2), preserve old published assets, document verification and make the tested patch available through the normal release/update feed.

All native checks run on a separate desktop without installing shell hooks, recording audio, using accounts or touching the running SharpVoice app.

Verification: old implementation failed the TOPMOST assertion. Fixed implementation passed native start/status/repeat stacking, independent editing focus/foreground, click-through/NOACTIVATE styles, geometry and dismissal on an isolated desktop. Direct2D pill rendering passed both themes; full offline suite passed 179/179; build had zero warnings/errors. The first focus fixture accidentally focused its competing window during creation, before showing the pill; reset after fixture creation and a pre-show assertion resolved that test setup issue without extra product changes. Independent read-only review found no actionable issues. CI native package/lifecycle verification and publication remain.
