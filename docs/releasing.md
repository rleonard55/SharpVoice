# Building and Publishing SharpVoice

Publish a new version only when the owner explicitly requests a release. Routine changes and successful local builds do not authorize a version bump, release workflow dispatch or publication.

## Windows x64 packages

Install the .NET 10 SDK, Visual Studio 2022 C++ build tools and the Windows SDK. From the repository root:

```powershell
./scripts/Build-Release.ps1 -Version 0.1.10
./scripts/Test-ReleaseLifecycle.ps1 -ReleaseDirectory artifacts/releases/0.1.10
```

`Build-Release.ps1` runs the complete offline suite through its Windows executable, publishes Native AOT with mapped source paths and native debug symbols disabled, checks that the executable contains neither the builder's project path nor home path, verifies its non-recording bootstrap/version command, then packages only the executable, verified Tiny Q5 English model, SharpVoice MIT license and public redistribution notices. The model is fetched from the official whisper.cpp Hugging Face repository into ignored build cache and SHA-256 checked before packaging. A native `--verify-bundled-model` check uses an isolated empty cache and blocks HTTP to prove first-run resolution works offline. The pinned SDK and packaging tool are both Velopack 1.2.161. `-SkipTests` is for local packaging iterations after a fresh complete test run; CI never uses it.

The default installer, portable ZIP and full update package all include `models/ggml-tiny.en-q5_1.bin` beside the executable. This adds about 31 MiB before packaging compression; users need not download that model on first use. Other selections retain on-demand model downloads. Model binaries are ignored by Git and enter only release assets. Published 0.1.0 assets remain unchanged.

Packages appear in `artifacts/releases/<version>/`:

- `SharpVoice-win-x64-Setup.exe`: per-user installer with a Start Menu shortcut, upgrades and uninstall. No administrator rights or separate .NET runtime required.
- `SharpVoice-win-x64-Portable.zip`: self-contained portable distribution with updater metadata.
- `SharpVoice-<version>-win-x64-full.nupkg`: verified full update package.
- `releases.win-x64.json`, `assets.win-x64.json`, `RELEASES-win-x64`: metadata required on the same GitHub release.
- `sharpvoice-vscode-<plugin-version>-win32-x64.vsix`: the same IDE plugin bundled in the desktop app.
- `SHA256SUMS.txt`: asset hashes.

Close a development copy from its tray before reviewing an installed version; both copies intentionally use the same single-instance mutex. Installations preserve the user's `.sharpvoice` preferences, protected credentials and model cache. Uninstall does not erase that directory. A saved Start on Sign-In setting is refreshed to the installed stable launcher. Uninstall removes only an exact startup command belonging to that installation.

No signing certificate is configured. Current packages are unsigned; Windows can display an unknown-publisher or SmartScreen prompt. Configure publisher code signing before requiring a trusted-publisher installation experience. Never bundle a certificate/private key, GitHub token, user config, diagnostics, models or PDBs. The native app retains its existing x86-64-v3 (AVX2/FMA) hardware requirement.

## Automatic updates

Open **Settings → General → Updates**. Two separate persisted settings control scheduled updates:

| Setting | Default | Behavior |
|---|---|---|
| Automatic Updates | On | Checks 30 seconds after startup, then every six hours. Off cancels automatic work and blocks pending automatic installation. |
| When an Update Is Found | Install When Idle | Notify Only checks/notifies; Download Automatically also downloads/verifies; Install When Idle additionally restarts after 60 quiet seconds. |

The action remains selectable and remembered while Automatic Updates is Off. Check Now only checks; Download and Install And Restart require an available update. Install And Restart downloads and verifies the update when needed, then applies it while no voice/AI/editor/authentication/speech or plugin-update work is active. Automatic installation also waits for closed settings, answer/review and tools windows, completed wake preparation, no wake-prefix inference and observed VAD silence. Notifications defer until voice work is idle. Voice and authentication workers are disposed before replacement. Restart clears memory-only conversations; settings and models survive.

Velopack's implicit apply-on-startup is disabled: a previously downloaded package cannot bypass Off or Notify Only on the next launch. Only newer versions on the installed `win-x64` channel are considered; normal operation does not downgrade or switch channels.

The client uses the fixed public feed `https://github.com/rleonard55/SharpVoice`, excludes prereleases/drafts and contains no GitHub credential. Private repositories and drafts are invisible to it. Feed/network failures are nonfatal and appear in Updates. Publish both the repository and an approved release before expecting public update checks to work.

## Draft release workflow

The pinned GitHub Actions workflow runs on a `vX.Y.Z` tag or a manual version dispatch. It builds/tests/packages on Windows, tests isolated install/update/uninstall and creates or updates a **draft** GitHub release. A published version is never overwritten. Add `docs/releases/vX.Y.Z.md` before building each version. The workflow uses only its scoped GitHub Actions token to upload assets; no token enters the application. It never changes repository visibility or publishes the draft.

Before publishing the draft, review asset hashes, notes and the latest source/history privacy scan. Test the real installer on Windows 10/11, Start Menu launch, dictation/provider setup, sign-in startup, an update and uninstall/data preservation. The automated lifecycle fixture uses a separate random identity and install directory, upgrades from 0.1.0 to 0.1.1, rejects a corrupted update and a downgrade, then uninstalls itself. It never captures audio, reads account data or replaces a running SharpVoice instance. It also checks the actual native portable package through `--release-info` without opening the microphone.
