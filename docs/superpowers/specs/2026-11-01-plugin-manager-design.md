# SharpVoice Plugin Manager

Requested: install SharpVoice companion plugins into editors from SharpVoice.

Use General → Plugins to open the existing themed tools window, avoiding a seventh cramped settings tab. List VS Code, VS Code Insiders and Antigravity IDE. Show editor discovery, bundled and installed plugin versions, status, and explicit Install / Update / Reinstall actions. Refresh rechecks status. Installation does not depend on Apply Settings or change AI preferences. Show completion guidance to reload the editor.

Bundle the trusted local VSIX in the executable. Use each editor's own CLI to list and install extensions, with structured process arguments, a timeout, bounded output, cancellation and no shell. Never manipulate extension directories directly. Only the user's Install action changes their editor; opening the manager only reads status. Missing editors receive clear guidance. Antigravity requires a VS Code-compatible desktop IDE installation and is marked experimental until a real host test passes. Its Google account CLI is separate and must never be used as an extension installer.

Alternatives: manual VSIX instructions retain the current installation friction; a separate Plugins top-level tab crowds the existing navigation. The owned manager fits existing writing tools and keeps space for later integrations. These layout and implementation choices are the implementer's calls under the user's requested scope.

Test discovery, version handling, scoped CLI commands, bundled package identity, failure and cancellation using fixtures. Exercise a real install only in isolated profiles, never the user's editor. Every code change continues to require preview; add Antigravity process recognition without bypassing source/window validation.
