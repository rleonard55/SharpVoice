# Branded OAuth result page implementation plan

> Execute inline with superpowers:executing-plans.

**Goal:** Replace plain callback receipt text with a polished, responsive SharpVoice success/failure page that reports the real saved sign-in outcome.

**Architecture:** Keep the existing loopback PKCE flow. Exchange and persist credentials before sending success HTML; failures receive static safe explanatory HTML. Browser disconnection after a completed save must not undo login or cause a false failure. Render an embedded standalone template with microphone branding, clear icon/status/title, system light/dark colors and accessible text. No external assets or provider/query data in the HTML; remove callback parameters from browser history with a tiny local script. Token exchange uses the overall sign-in timeout; slow request-header reads retain their separate bound.

**Tech stack:** .NET10/AOT, embedded HTML/CSS/inline SVG, existing TCP loopback server.

- [x] Add isolated loopback/auth fixtures (no browser, real credentials, port1455 or session file) demonstrating the early-success problem and persistence failure behavior.
- [x] Add embedded result-page renderer and secure no-store HTML HTTP responses; integrate real outcome handling and best-effort browser delivery.
- [x] Verify success/save-failure/exchange-failure/decline/invalid callbacks, completion ordering, cancellation and response framing. Inspect branded previews in light/dark where headless rendering is available.
- [x] Managed checks and strict app/test AOT publish/checks; review, document and stage update.

Verified: premature success reproduced before fix; 63/63 managed/AOT checks passed; embedded page exports match; headless desktop light/dark and true390px layout inspected. Strict publishes warning-free, independent review found no Critical/Important issues. Real login credentials were not changed; provider browser approval/denial remains manual.
