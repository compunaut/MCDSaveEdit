# Proposed fork update plan

Date: November 1, 2026. Planning only: no application changes, remote fork, commits or pull requests were created.

## Recommended direction

Keep the WPF application and existing game save format. Establish a buildable Windows baseline first, then move the maintained fork to SDK-style .NET 10 for Windows. Keep parser/model code separate from the UI so most compatibility and malformed-input tests can run independently. Retain x86/x64 only where tested; preserve Steam Deck/Wine compatibility as an explicit validation target rather than assuming a modern runtime will behave identically.

.NET 10 is the current LTS choice with support into November 2028. Starting a new migration to .NET 8 or 9 provides little support runway: their announced end of support is November 10, 2026. A temporary .NET Framework 4.8 branch is reasonable for baseline comparisons or if Wine compatibility blocks migration; do not claim that 4.8 is automatically unsupported.

Sources: [Microsoft support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [8/9 retirement notice](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/).

## PR 1 — Establish a reproducible baseline

1. Base the implementation checkout on audited commit `58fe34e` and preserve exact submodule revisions. Use an ordinary fork checkout, leaving `../upstream` as an audit reference. Change submodule remotes to accessible HTTPS locations; when parser changes begin, own the modified submodule revisions or vendor the required source with history/notices.
2. Make a clean Windows build possible without developer-machine paths or analytics credentials. Supply a documented example config and an offline/no-game-content mode; load a user-supplied game-content key from local configuration when game assets are needed. Never require an analytics secret for compilation.
3. Replace the hardcoded Windows.winmd location with an appropriate SDK-resolved reference. Establish Debug and Release builds of the current target before large dependency/UI changes.
4. Split self-contained unit tests from opt-in tests requiring installed copyrighted game assets. Add sanitized, redistributable fixtures for supported save variants. Remove hardcoded account IDs/paths from tests.
5. Add Windows CI with read-only permissions, pinned action commits, no secrets in PR builds, test artifacts and dependency inventory. Record the baseline failures rather than silently ignoring them.

Acceptance: a fresh checkout builds in CI and runs portable fixture tests without private files or an installed game. The audit snapshot and hashes remain reproducible.

## PR 2 — Remove telemetry and make saving recoverable

Remove GameAnalytics, remote config polling, related keys, SQLite native resources and unused dependencies. Keep error reporting local with path/content redaction. Offer an optional manual update check against the fork's fixed HTTPS release endpoint, with a timeout, cancellation and defensive version parsing. The editor should open/edit/save without network access. Keep local diagnostics bounded.

Implement one shared transactional writer for JSON, encrypted saves and conversions: serialize completely, write a same-directory unique temp file, flush, validate, retain a backup, and replace the destination only after success. For new files, use collision-safe creation. Do not overwrite the original on failure, and do not report success or change the active file path after a failed save. Define behavior for a destination changed externally during editing.

Fix the filename collision algorithm, async stream lifetime, and detached TaskCompletionSource startup paths. These are small enough to land before or alongside runtime migration.

Acceptance: tests inject serialization/write/replace failures and show the original is unchanged and backup recoverable; repeated conversions remain in the chosen directory; no GameAnalytics/SQLite components remain in packaged output; a Windows offline smoke test succeeds.

## PR 3 — Modernize projects and dependencies

Convert to SDK-style projects and PackageReference, targeting net10.0-windows for WPF and an appropriate modern target for the core. Pin the SDK in global.json; centralize package versions, enable lock files and locked restore, and restrict restore to intended feeds. Remove compatibility packages that modern .NET already supplies rather than upgrading every legacy System.* package individually. Replace old Windows API Code Pack functionality or isolate the minimum maintained alternative.

Dependency decisions based on the captured NuGet feed:

| Component | Current parent solution | Proposed action |
|---|---|---|
| System.Text.Json | 6.0.6 | Prefer the serviced .NET 10 runtime implementation; if an explicit package is required, use the compatible current 10.0.x service release. Captured package latest: 10.0.14. |
| System.Text.Encodings.Web and BCL shims | 6.x and older | Prefer framework references; remove redundant declarations and stale binding redirects. |
| SkiaSharp + Win32 native assets | 2.88.3 | Upgrade as one version-matched set to the current maintained stable line that passes validation. Captured NuGet latest: 4.154.0; release/support documentation and feed can differ, so verify support and availability again when implementing. Remove macOS assets from the Windows package. Historical 2.88.6 only fixes the recorded CVE; it is not the final target. |
| Checked-in native Skia DLLs | Unknown independent provenance | Remove manual binary inputs; consume official pinned matching native assets and verify packaged hashes/architecture. |
| DotNetZip | 1.16.0 | Remove. Replace the PAK zlib/gzip streams with System.IO.Compression.ZLibStream/GZipStream and test exact output and encrypted block boundaries. |
| GameAnalytics + SQLite | 3.3.5 + SQLite resource 3.12.0 | Remove. |
| Newtonsoft.Json | 13.0.1 | First assess removal of PakReader's JSON-export feature if unused. If retained, update to 13.0.4 (captured stable), without enabling input-controlled type names. |
| System.Reactive | 5.0.0 | Review usage and upgrade to a compatible maintained version; captured stable: 7.0.0. Verify subscription disposal and UI scheduling. |
| NLog | 5.0.4 | No C# usage found; remove if truly unused, otherwise evaluate 6.2.2 (captured stable) and use redacted local logging. |
| Fody / Costura | 6.6.4 / 5.7.0 | Prefer standard .NET publish output to reduce custom assembly/native embedding. If retained, review build-time behavior and evaluate 6.9.3 / 6.2.0 (captured stable). |
| MSTest | 2.2.10 | Upgrade framework/adapter together and include the appropriate test SDK; captured MSTest stable: 4.5.2. |
| Nested standalone submodule manifests | Newtonsoft 12.0.3, Skia 1.68.1.1, net5.0 target | Retire unused standalone build routes or update them coherently. Prevent CI from accidentally auditing/building a different dependency graph. |

These are candidate versions, not a promise that all major-version upgrades are source compatible. Re-run feeds at implementation time, resolve the actual graph, inspect native contents, and run build/tests before freezing versions. Do not turn 'latest package' into an unreviewed mass update.

Acceptance: clean locked restore/build, no unexplained high/critical advisories in the resolved direct or transitive graph, matching managed/native image libraries, both supported Windows architectures exercised, and baseline save behavior preserved. Track any documented non-reachable advisory exception with rationale and expiry instead of suppressing warnings wholesale.

## PR 4 — Harden parsers and preserve save data

- Replace brace-count JSON extraction with token-aware first-value parsing, including quoted braces/escapes, UTF-8 and supported console trailing data. Reject malformed control characters rather than stripping arbitrary bytes silently.
- After upgrading JSON, preserve unknown fields through JsonExtensionData or a JSON-document editing approach at all relevant nested model boundaries. Validate semantic round trips rather than whitespace/order. Distinguish intentional edits from accidental field deletion.
- Introduce configurable but finite file-size, JSON-depth, collection-count, PAK index/entry, decoded-byte and texture-pixel limits. Choose limits from legitimate fixtures; initially document conservative bounds and how oversized files are handled. Use checked arithmetic and ensure every offset/length fits both the file and remaining budget.
- Replace single Read assumptions with exact-read loops; verify decompression lengths, block boundaries and archive consistency before passing bytes to consumers. Support cancellation and ensure malformed data cannot strand the UI.
- Fix texture pixel ownership: validate buffer/stride requirements, then copy into bitmap-owned memory or retain explicitly owned pinned/unmanaged storage for the full native lifetime. Stress under GC and reject inconsistent raw/DXT/BC buffers before native calls.
- Keep the game's AES key/mode/header format compatible. Refactor crypto object/stream ownership if needed; do not switch cipher modes or add incompatible bytes to saved files.

Acceptance: deterministic tests for truncated headers, invalid UTF-8, braces/escapes in names, duplicate console tails, unknown nested fields, large counts/overflowing dimensions, invalid offsets and decompression lengths. Native stress tests and bounded fuzz runs must end with controlled errors rather than access violations, uncontrolled allocations, hangs or save loss. Windows UI smoke tests must surface errors and allow retry.

## PR 5 — Package and release the maintained fork

Choose the fork owner/name and update release links, branding and about text consistently. Preserve upstream credits and complete a distribution-license review for DungeonTools AGPL and FModel-derived material. Include required source/notices with binary distribution as applicable.

Publish through CI with a pinned SDK, locked dependencies, minimal token permissions and separated release authorization. Generate an SBOM including native components, SHA-256 checksums and source/submodule revision records. Consider code signing; never treat signing as a substitute for review. Verify the release artifact's managed/native versions and actual loaded DLL paths, not just manifests. Compare executable behavior to the audited sources and inspect packaging for unexpected files. Publish the test/support matrix and recovery instructions.

Acceptance: reproducible source inputs, verifiable release hashes and provenance, no telemetry by default, successful restore-from-backup drill, validated Windows x64/x86 targets if shipped, and an explicit Steam Deck/Wine result (supported or documented unsupported). No release should be labeled hardened while the native/source-to-binary validation remains open.

## Suggested scope for the first milestone

Deliver a buildable offline editor with transactional saves and portable fixtures first (PRs 1–2). Complete runtime/dependency migration and parser hardening before the first broadly distributed hardened release (PRs 3–5). Keep feature additions outside this milestone so save compatibility and security changes remain reviewable.
