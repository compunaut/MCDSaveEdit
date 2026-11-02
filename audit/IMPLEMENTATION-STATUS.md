# Personal fork implementation and validation

Date: November 1, 2026. Local implementation complete; hosted Windows CI and manual game/UI compatibility checks remain pending.

## Scope and repository state

The local branch is `codex/personal-fork-hardening`, based on audited upstream commit `58fe34efa4cd458992f7dc5af7258450f2e4a1bf`. Changes are staged for review, not committed or pushed. No Git remote is configured and no remote repository has been created. Nothing has been sent to the original owner. The ignored `upstream/` audit checkout remains unchanged.

Three sub-agents implemented independent save-safety, PAK/native, and CI work. The parent integrated and compiled their changes. Agents then reviewed each other's work; this caught an introduced wrong-AES-key retry regression, which was fixed and covered with an encrypted synthetic PAK test before final validation.

## Implemented

- SDK-style .NET 10 application and wrapper projects; exact SDK 10.0.403, central package versions, checked-in dependency locks, a restricted NuGet source, and fail-closed advisory checks.
- Removed GameAnalytics, remote configuration/update lookups, DotNetZip/Ionic compression, Costura/Fody embedding, native SQLite, redundant old compatibility packages, machine-specific Windows metadata paths, and the private Secrets.cs build requirement.
- Updated SkiaSharp/official native assets to 4.154.0, Newtonsoft.Json to 13.0.4, Reactive to 7.0.0, and MSTest to 4.5.2. Built-in runtime JSON and compression replace obsolete packages. The Windows folder picker uses WPF OpenFolderDialog. Optional game assets use a locally supplied environment key; save editing is offline.
- Same-directory staged writes, flushed contents, atomic replacement, unique recoverable backups, collision-safe conversion names, and pre-commit content snapshots that reject stale saves. Save exceptions are handled at the UI boundary, and success metadata updates only after a successful write.
- Correct async stream ownership, per-operation AES instances with unchanged game-format key/mode/padding/header, bounded input, strict UTF-8, quoted-brace-safe first-object parsing, and preservation of unknown fields in nested save models and item copies.
- Skia-owned pixel buffers, texture dimension/buffer validation, exact reads, bounded archive strings/arrays/tables/ranges, exact decompression, index hash validation, and independent AES operations in the PAK reader. Invalid keys are rejected without aborting later key attempts.
- Vendored parser source and original license notices, so local fixes do not require upstream pushes. Obsolete standalone submodule manifests were retired; parent wrapper projects are the build entry points.
- Secure CI with read-only permissions, immutable official action pins, no stored checkout credentials, no secrets/privileged PR workflows, no automatic publishing, locked restores, dependency audits, Debug/Release tests, x64/x86 publishes, native provenance checks, and a single aggregate Final validation gate.
- Published-executable `--verify-native <report.json>` mode exercises production texture decoding/GC/PNG encoding and asserts the actual loaded Skia module came from the publish directory. CI runs it under both matching Windows architectures and retains JSON evidence.

## Local validation results

The consolidated run in `artifacts/final-validation/results.json` passed:

| Check | Result |
|---|---|
| Locked solution and portable-test restores, advisory failures treated as errors | Pass |
| Production-source security tests | 21 passed, 0 failed, 0 skipped |
| Debug solution compile, including Windows test assembly | Pass |
| Release solution compile, including Windows test assembly | Pass |
| Windows x64 and x86 framework-dependent publishes | Pass |
| Signed NuGet canonical-content integrity, native byte matching, PE architecture, removed-component rejection, and output hashes | Pass for both architectures; 14 files per package |
| CI-validator unit/integration tests, including actual signed-package tampering and wrong locked hashes | 16 passed |
| Resolved direct/transitive NuGet vulnerability JSON gate | Pass; no reported vulnerable packages |

Portable tests execute the exact production parser, crypto, atomic writer and PAK source. They cover real public Blank.dat decrypt/edit/encrypt round trips, independent OpenSSL ciphertext, unknown nested fields, malformed UTF-8, raw controls, BOMs, quoted braces, incomplete JSON, partial header reads, output limits, backup recovery, staging/replacement failures, conversion collisions, stale saves, invalid archive/texture ranges, incomplete decompression, encrypted wrong-key-then-right-key behavior, and 32 valid native texture decodes with compacting GC and pixel checks.

Native provenance validation uses NuGet's verifier from the pinned SDK. Signed-package canonical content hashes differ from raw signed ZIP hashes; both archive integrity and locked content hash are checked before comparing extracted/published native bytes. This establishes correspondence to locked package inputs, not an independent malware audit of upstream native binaries.

Evidence is local under `artifacts/final-validation/`: per-check logs, TRX, result JSON, and per-architecture native/file inventories and SHA256SUMS. Resolved dependency/advisory reports are in `artifacts/evidence/`. Generated output is ignored by Git. The published directories are `artifacts/win-x64/` and `artifacts/win-x86/` and need the matching serviced .NET Desktop Runtime on Windows.

## Not yet validated / remaining release gates

- This host is macOS. Windows application/test assemblies cross-compile here, but the WPF UI, full Windows test suite and published-executable native-load modes have not executed here. They are configured as required Windows CI jobs; no hosted CI run has occurred without a remote repository.
- Real installed-game encrypted PAK compatibility, console variants beyond available fixtures, and manual character loading after edits require disposable copies and the appropriate local game/key. The synthetic encrypted PAK test establishes key-retry behavior but does not substitute for a real game fixture.
- Steam Deck/Wine compatibility with .NET 10 is unverified. Existing upstream Steam Deck instructions are historical and must not be treated as validation of this fork.
- Builds retain inherited nullable/platform warnings (Release compile reported 270 warnings, 0 errors). This change does not claim a warning-free codebase or exhaustive fuzz/native analysis.
- The external-edit snapshot check is not an OS-level compare-and-swap. Another writer can race between the final check and replacement; unique backups retain the replaced original for recovery. Avoid editing the same character simultaneously in the game and editor.
- Code signing, a standards-compliant SBOM, a full distribution-license review, and release publication are not complete. Do not label or distribute these review artifacts as a fully validated hardened release.

## Next hosted step

Choose the personal repository destination/visibility, push only there, and require **Final validation** in that repository's branch protection. The CI workflow never pushes back or opens PRs against CutFlame/MCDSaveEdit. Complete manual Windows/game/Wine checks before any release.
