# OAuth reconnect without forced logout

> Execute inline with superpowers:executing-plans.

**Problem:** Saved-session presence makes Settings expose only Sign out, UI guards reject another sign-in, and the app's SignIn handler returns early whenever a session exists. Users must discard their login before they can initiate a replacement. Browser SSO is a separate possible symptom; a clarification is pending. The current running executable also predates the staged truthful-callback fix.

**Design:** Preserve one provider-specific account control at a time. Signed out shows Sign in; signed in shows an Account menu with Sign in again and Sign out. Reconnect always starts the provider's owned flow without first deleting credentials. ChatGPT credentials remain until new exchange and persistence succeed; cancellation or failure preserves them. Claude Code retains control of its own credentials. Busy/hidden/backend-invalid actions remain blocked. ChatGPT and Claude keep independent actions, and active dictation is cancelled before reconnecting. No undocumented force-login query flags, browser cookie changes, or live token invalidation are introduced.

- [x] Regression for signed-in account management policy and safe reconnect guards.
- [x] Native themed account menu, provider dispatch and app guard repair.
- [x] Isolated successful/cancelled/failed replacement fixtures proving old login preservation and new flow initiation.
- [x] Managed/native checks, source review, documentation, and staged update. No real sign-out or credential changes.
