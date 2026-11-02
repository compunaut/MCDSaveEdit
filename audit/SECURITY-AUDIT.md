# MCDSaveEdit security audit

Date: November 1, 2026. Status: source/declaration audit complete; release certification and runtime validation remain open.

## Conclusion

The inspected source is consistent with a Minecraft Dungeons save editor. I found no affirmative evidence of credential theft, persistence, a hidden downloader, or arbitrary command execution in the inspected application and pinned submodule source. That is not a guarantee that upstream release executables are safe: they were not downloaded, executed, decompiled, or matched to this source.

The project needs security and data-integrity work before a hardened fork release. Highest priorities are unsafe texture-buffer handling, native dependency provenance, automatic telemetry/error reporting, and non-atomic save overwrites. Three declared packages in the parent solution match current NuGet advisories; their advisory-specific exploit paths are not all demonstrated here.

The prior shared chat was used for its stated objective: audit CutFlame/MCDSaveEdit and plan a maintained fork. Its two assistant response bodies were absent from the visible shared page, so no prior vulnerability claims were assumed true.

## Baseline and method

The initial workspace contained an empty Git repository. An untouched shallow upstream checkout was downloaded into `../upstream` with both submodules at the exact gitlink commits:

- MCDSaveEdit master: `58fe34efa4cd458992f7dc5af7258450f2e4a1bf`.
- DungeonTools: `741372821cf7015eea67127a0e8082d600f11f7a`.
- PakReader: `b3d9b07db95c5739d85e909c75864c0f46e4b552`.

Reviewed application startup, networking, telemetry, process launches, registry use, file import/export, JSON converters, save encryption, package/project/build declarations, PAK decompression and texture handling, native binary resources, and test/build setup. Searched for process execution, network clients, dynamic assembly loading, unsafe deserialization, and native interop patterns. Focused code inspection is not a formal proof of every parser branch.

`scan_dependencies.py` parsed 98 dependency declarations across all checked-out projects: 95 in the parent solution's four packages.config files, representing 71 unique package IDs; three additional PackageReference declarations in standalone submodule projects. Queried NuGet's complete published vulnerability index and stable-version feeds. Results and source-feed timestamps are in `dependencies.json`; all versions are listed in `DEPENDENCIES.md`. This is a declaration scan, not a restored transitive dependency graph. Numeric stable-version comparison is sufficient for the declarations examined; the script rejects unsupported version forms rather than guessing.

No project/package build code or downloaded executable was run. This macOS host has no `dotnet` command, and the solution is Windows WPF targeting .NET Framework 4.8 with a missing local Secrets.cs and a machine-specific Windows SDK reference. C# builds, automated tests, runtime exploit probes, native load resolution, telemetry traffic capture, fuzzing, and release-to-source matching were not performed. Do not describe this as a clean runtime or binary audit.

## Findings

### F1 — High priority: unsafe native texture buffers (source-confirmed defect; exploitability unverified)

`PakReader/PakReader/TextureDecoder.cs:48–56` pins a managed byte array, assigns its pointer through `SKBitmap.SetPixels`, exits the fixed block, then calls `SKImage.FromBitmap`. SetPixels replaces the pixel address; it does not establish a managed-array pin. A moving GC can invalidate the address before the native consumer finishes. In raw pixel formats, array size is not checked against width, height, color type, and row stride. Malformed PAK texture metadata can therefore reach native code with an inconsistent buffer size. Several decoder allocations multiply untrusted dimensions without checked arithmetic.

Reachability: `MCDSaveEdit/Services/ContentResolvers/PakIndexExtensions.cs:30` → `UTexture2D.Image` → `TextureDecoder.DecodeImage`. User-selected or modified game PAKs are the input boundary. A legitimate local installation reduces attacker exposure, but the application allows choosing other directories.

Impact: potential out-of-bounds native reads, invalid pointers, and crashes; memory corruption or code execution has not been demonstrated. Fix by validating dimensions, pixel counts, required byte length and format-specific block sizes with checked arithmetic, then copying into bitmap-owned memory or using an explicitly owned buffer whose lifetime covers every native consumer. Package upgrades alone do not fix this code.

Reference: [SKBitmap.SetPixels API](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skbitmap.setpixels?view=skiasharp-2.88).

### F2 — High priority: save truncation can destroy the original (confirmed data-integrity defect)

`MCDSaveEdit/ViewModels/MainViewModel.cs:222–232` opens the destination with FileMode.Truncate and copies the replacement directly. An interrupted write, disk-full failure, or process crash can leave the only save empty or partial. `Logic/FileProcessHelper.cs:32` similarly creates/truncates conversion output. No automatic backup or atomic replacement is present in these write paths; the README asks the user to back up manually.

Serialize/encrypt completely, write a uniquely named temporary file in the destination directory, flush and validate it, create a recoverable backup, then use an appropriate atomic replace/move strategy. Preserve the original on every pre-commit error and report save failure accurately. Use collision-safe creation and avoid silent fallback to direct truncation.

### F3 — Medium privacy priority: automatic release telemetry and raw error messages

`Services/EventLogger.cs:12–27` enables submission in non-DEBUG builds, initializes GameAnalytics and starts a session during `App.xaml.cs:35`. There is no application-level opt-in check in this path. Events include UI activity, item/enchantment identifiers, and error messages. `MainViewModel.cs:175` forwards exception.ToString(); `FileProcessHelper.cs:16` can include a full filesystem path; `PakIndexExtensions.cs:19` includes an asset path. These can expose names, local paths or other context through diagnostics.

This is visible analytics integration, not evidence of a covert credential stealer. Actual successful transmission depends on valid keys and SDK behavior; no live traffic was captured and SDK internals were not audited. The source checkout does not include Secrets.cs. Remove GameAnalytics and its native SQLite dependency for the fork, use local redacted diagnostics, and make update checks explicit and optional.

### F4 — High package severity: vulnerable dependencies, with reachability qualifications

| Declared package | Advisory | Source exposure and action |
|---|---|---|
| SkiaSharp 2.88.3 | CVE-2023-4863 / GHSA-j7hp-h8jx-5ppr; affected 2.x before 2.88.6 | Native image library is used. This specific advisory concerns WebP decoding; the inspected game-texture path decodes raw/DXT/BC pixels and encodes PNG, so an attacker-controlled WebP decode path was not established. Upgrade managed and native packages together, and remove stale embedded copies. 2.88.6 is only the historical fix floor, not the recommended maintained target. |
| System.Text.Json 6.0.6 | CVE-2024-43485 / GHSA-8g4q-xg66-9fp4; affected 6.0.0–6.0.9 | Save JSON is untrusted, but this advisory requires JsonExtensionData. No JsonExtensionData use was found in the checked-out C# source. Record as a vulnerable declaration with no demonstrated advisory-specific path. Historical fix is 6.0.10; move to a serviced release rather than retaining the obsolete line. |
| DotNetZip 1.16.0 | CVE-2024-48510 / GHSA-xhg6-9j5j-w4vf; no patched DotNetZip release | PakReader uses Ionic.Zlib.ZlibStream/GZipStream in FPakEntry.cs:190–191. No ZipEntry extraction use was found, so the directory-traversal advisory is not shown reachable. Remove the abandoned dependency, preferably using built-in zlib/gzip on modern .NET. |

Sources: [Skia advisory](https://github.com/advisories/GHSA-j7hp-h8jx-5ppr), [Microsoft JSON advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-8g4q-xg66-9fp4), [DotNetZip advisory](https://github.com/advisories/GHSA-xhg6-9j5j-w4vf), [DotNetZip maintenance notice](https://www.nuget.org/packages/DotNetZip/1.16.0).

The nested standalone PakReader.csproj additionally declares Newtonsoft.Json 12.0.3, matching GHSA-5crp-9r3c-p9vr (fixed in 13.0.1), plus SkiaSharp 1.68.1.1. These are not the project used by MCDSaveEdit.sln: the parent wrapper compiles submodule source files with Newtonsoft.Json 13.0.1 and SkiaSharp 2.88.3. Do not count the old Newtonsoft package as a fourth active solution vulnerability. Fix or retire the standalone manifests when maintaining the submodule.

No additional advisory matches were returned for the other declared versions in the captured feed. This says nothing definitive about unlisted native components, package internals, or an unresolved dependency graph.

### F5 — High maintenance priority: embedded native DLLs bypass manifest-only upgrades

`MCDSaveEdit.csproj:558–561` explicitly embeds both architectures of libSkiaSharp.dll and sqlite3.dll. FodyWeavers.xml enables Costura. These DLLs are independently tracked binary inputs, so bumping packages.config does not replace them automatically. Exact selection/loading behavior must be verified in a packaged Windows build.

Both SQLite files contain resource version `3.12.0`, SourceId dated 2016-03-29. This is an age/provenance finding; no SQLite CVE is asserted as reachable. Skia file provenance/version was not independently established. SHA-256 hashes and extracted hints are saved in `native-binaries.json`. Do not equate their names or package references with verified binary contents. Remove telemetry/SQLite; source any retained Skia native files from the matching pinned official package, with build-time hash checks and packaged-artifact inspection.

### F6 — Medium: uncontrolled file sizes, counts, offsets and decompression metadata

`ReaderExtensions.cs:12–56` allocates strings and arrays directly from input counts. `FPakEntry.cs:135–196` allocates from archive sizes and uses offsets/block counts without comprehensive file-bound checks or global budgets. Reads assume requested bytes are available; the decompressor uses one Read per block although a Stream may return fewer bytes. Save decryption copies the whole input to memory, and ProfileParser creates another in-memory copy before deserializing. JSON's existing default depth limit is not a substitute for byte/count limits.

Impact: crafted/truncated files can cause large allocations, exceptions, parser inconsistencies or application failure. Add bounded readers, exact-read loops, checked arithmetic, compressed/uncompressed byte and ratio budgets, cancellation, and input-format validation. Handle failures at the import boundary. Treat native texture concerns separately under F1.

### F7 — Medium data-integrity/reliability defects in import and conversion

- `Data/Profiles/ProfileParser.cs:27–58` counts braces without tracking JSON strings or escapes. A valid string containing `}` can terminate the extracted object early. It also strips every control byte, which may silently alter malformed content instead of reporting it. Use token-aware parsing of the first JSON value while retaining the intended console trailing-data compatibility.
- `Logic/FileProcessHelper.cs:73–75` uses Substring(length - extension.Length) when resolving a filename collision. An existing `C:\Saves\hero.json` becomes relative `.json_1.json`, losing the selected directory and base name. Build candidates from the original directory/stem and use FileMode.CreateNew to avoid races.
- `ViewModels/MainViewModel.cs:133–137` returns a parsing Task from a using scope without awaiting it. The stream can be disposed while asynchronous deserialization still needs it. Await within the ownership scope, and clarify parser/crypto stream ownership consistently.
- Typed models have no JsonExtensionData preservation. Unrecognized future save fields can be dropped on a read/write cycle. Preserve unknown fields at relevant model boundaries and compare semantic round trips. Upgrade System.Text.Json before introducing JsonExtensionData because doing so would make F4's current vulnerable feature reachable.

`static-reproductions.json` contains deterministic Python reproductions of the brace and collision algorithms. These illustrate the source defects; they are not execution tests of the C# application.

### F8 — Medium reliability: background failures can leave startup waiting forever

`Services/ContentResolvers/PakContentResolver.cs:94–102` runs work in Task.Run but returns a separate TaskCompletionSource task. If loadPakFiles throws, the completion source never completes and the caller can hang. Similar detached completion-source patterns deserve review. `GameAnalyticsRemoteConfig.cs:20–23` also spins and logs continuously until a five-second timeout, and Config.cs parses remote version strings with new Version rather than TryParse.

Return/await the actual worker task, propagate exceptions, support cancellation, and use bounded waits. Remove the analytics config coupling; parse any update metadata defensively.

### F9 — Build, supply-chain and distribution gaps

The parent solution targets .NET Framework 4.8. That is not inherently unsupported; support depends on the Windows host lifecycle. Nested DungeonTools targets .NET 5 and nested PakReader netstandard2.1, but these standalone projects are bypassed by the parent wrapper projects. Do not mistake these for the running app's target framework.

The Windows metadata reference is a hardcoded relative path into Windows Kits (`MCDSaveEdit.csproj:273`), Secrets.cs is intentionally absent, packages use old explicit HintPaths/imports, and no CI/security update workflows or dependency lock files were found. Existing tests rely partly on installed game content, local account paths, and fixtures missing from the public checkout. Latest upstream release metadata identifies 1.5.9.0, published December 9, 2022, while this source snapshot includes later changes. The release asset has no digest in the queried GitHub metadata; that alone does not establish whether the executable is signed.

Fork builds need a clean Windows CI baseline, reproducible dependency resolution, native provenance checks, automated advisory checks, and release checksums/source attribution. Review license obligations before distribution: the root is MIT, DungeonTools includes AGPL-3.0, and README credits FModel-derived material under GPL-3.0. Preserve notices and obtain a proper distribution-license review rather than presenting the combined fork as automatically MIT-only.

## Behaviors inspected without a malicious-source finding

- Process.Start call sites open fixed donation pages and the configured constant GitHub release page following UI actions; no downloaded executable execution was found.
- RegistryTools accesses the application's HKCU Software settings for preferences/recent files; no autorun persistence path was found.
- Reflection-based JSON converter activation uses attributes on compiled model types, not input-supplied assembly/type names. No BinaryFormatter or TypeNameHandling enablement was found.
- Save encryption uses a static AES key and ECB because the game format requires compatibility. This is not meaningful confidentiality protection, but changing the algorithm would break saves; do not 'fix' it to GCM without changing the game format.

## Evidence and next validation boundary

Read `UPDATE-PLAN.md` for the proposed implementation sequence. Keep the upstream checkout untouched as the comparison baseline. Before a hardened release, require Windows build/test results, semantic save compatibility tests, malformed-input/native stress checks, resolved transitive scanning, artifact/native-load verification, and a source-to-release provenance check. Those are explicit release gates, not claims that these checks already passed.
