# Automatic Edge Voices

User correction: Edge voices must be prepared by SharpVoice; no manual installation or setup button. Existing Edge-with-Windows-fallback choice remains.

Bundle a pinned Windows x64 embeddable Python runtime and edge-tts wheels inside the executable. Extract into an app-private content-addressed cache on first speech use, verifying every file and repairing damaged files atomically. Run the private interpreter in isolation with the existing bounded stdin/audio/cancellation pipeline. No system installation, PATH changes, package manager, administrator access or first-use dependency download. Keep offline Windows fallback and visible preparation/reading status. Remove setup button and manual instructions; keep concise Microsoft data disclosure. Tests must use temporary caches and offline helper probes, not actual speech/accounts/microphone.

Approaches: a private bundled runtime meets the request without setup/network installation and reuses the maintained upstream client; runtime pip installation adds network/installation failure; reimplementing the unofficial protocol adds unnecessary maintenance. Bundling is the implementation choice under the user’s explicit request to handle setup in-app.
