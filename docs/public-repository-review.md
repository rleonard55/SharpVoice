# Public Repository Privacy Review

Reviewed 2026-11-02 (refreshed after installer/update packaging). This review concerns publishing the SharpVoice source repository; it is not a general application security audit.

## Result

No personal credentials were identified in the candidate public source or its bundled speech/editor packages. One development note contained a machine-specific user path; it now uses a portable build command. Python caches and additional credential, runtime-data, diagnostic and local-tooling files are excluded by `.gitignore`. The refreshed review also added `.aws/` to protect local AWS configuration/credentials.

The final candidate inventory contains **565 files**. Gitleaks reports **no leaks** after the narrowly scoped, reviewed public-value exceptions. All **35 credential/generated-file ignore checks** pass, and all **four synthetic canaries** are detected, including canaries inside tests and an archive. The refreshed text/binary privacy-pattern check inspected **836 bundled archive entries**, including nested package contents; no oversized entries were skipped. The original independent review covered 295 archive entries.

The source includes the product specification and the built-in prompt in `src/SharpVoice.Core/DefaultPrompts.cs`. These contain product instructions and generic examples, and will be public with the source. The reviewed test email addresses use reserved example domains. Third-party author emails and upstream build paths in redistributed packages are attribution/build metadata, not this developer's private account data.

## Git Boundary and History

Initially, Git resolved this folder to a repository rooted at the parent Documents folder. That repository had no reachable commits or refs; its object store contained dangling blobs. Those unrelated files/objects were not imported or published.

SharpVoice now has its own newly initialized Git repository. No commits, remote, staging or publishing were performed during the initial review. Subsequently, the reviewed source was committed and pushed to the explicitly requested private SharpVoice repository. History is scanned again before release-source publication; unrelated parent content is never imported. The parent repository was left unchanged. There is no SharpVoice commit history to scrub, and this review makes no claim about other Documents content, parent dangling objects, or any remote repository supplied separately.

The first private publication uses the account’s public GitHub noreply commit address. Continue using that identity to avoid exposing a personal email in future commit metadata.

## Latest Verification

The 2026-11-02 scan used a fresh copy of the exact Git-included candidate inventory, not the entire working folder. Gitleaks 8.30.1 completed with exit code 0 and zero findings using recursive archive/decoding options and full redaction. Four synthetic canaries in ordinary text, tests, an archive and the allowlisted manifest path were all detected. All 35 credential/runtime/generated-file ignore checks passed. Additional text/binary/archive checks found no developer username/home path, authenticated URL or private-key markers. Source publication is cleared by this privacy review; executables/PDBs and ignored local data remain outside its scope.

## Review Method

- Inspect the exact candidate file inventory using Git's ignore rules, including vendored source and the contents of the VSIX and speech ZIP. Search text and binary contents for personal user paths, email addresses, private-network URLs, authenticated URLs, private-key markers, common credential formats and literal secret assignments.
- Run checksum-verified Gitleaks 8.30.1 locally with its default rules, recursive decoding and archive scanning. No source or credentials are sent to an external scanning service.
- Review all 13 default-rule findings: 12 are SHA-256 entries in `vendor/upstream-files.sha256.json`; one is the speech library's public protocol constant. The latter was compared byte-for-byte with its checksum-verified public upstream wheel.
- Keep default detection enabled in `.gitleaks.toml`. Exceptions cover only complete checksum-entry lines in the specific upstream manifest and the exact known public protocol constant. Tests and dependency archives are not excluded wholesale.
- Verify ignore rules and the isolated Git boundary, and re-scan a fresh candidate-source copy after cleanup. Use throwaway synthetic credential-shaped canaries to verify that the exceptions do not disable detection elsewhere.

## Excluded Local Data and Release Assets

Build caches, local diagnostics, generated executables/symbols, downloaded models, credentials, runtime sessions/history and local agent/IDE state are excluded from the source publication. Existing ignored files were not deleted or declared safe to upload. User provider profiles outside the project were not opened or changed.

**Earlier development executable/PDB artifacts contain local build paths and remain excluded and uncleared.** The separately built 0.1.0 release assets use mapped paths, disabled native symbols and disabled SDK symbol copying. Their 63 package/archive entries and extracted executable strings were checked separately: no local builder paths or disallowed data files, matching asset hashes and zero secret findings. This clearance is specific to those inspected release assets, not all ignored artifacts. Do not publish a ZIP of the entire working folder or copy the parent `.git` directory.

## Repeat Before Publishing

Review the files about to be committed, then scan a clean copy of that candidate source with:

```text
gitleaks dir <clean-source-directory> --config .gitleaks.toml --max-archive-depth=3 --max-decode-depth=5 --redact=100
```

Once commits exist, also scan all local history:

```text
gitleaks git . --config .gitleaks.toml --log-opts=--all --redact=100
```

Keep scan output private until reviewed. A clean automated scan reduces risk; it cannot prove that every possible secret or personal detail is absent. This review applies to the inspected snapshot and needs repeating after new files, commits, archives or remotes are added.

Scanner/configuration reference: [Gitleaks v8.30.1](https://github.com/gitleaks/gitleaks/blob/v8.30.1/README.md).
