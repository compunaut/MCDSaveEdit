# Personal fork audit — November 2, 2026

Audited source: `compunaut/MCDSaveEdit` commit `2ee88a735cff48b13c5f81583937bc23274c7da2`, also the source for immutable release `v1.0.0`. Three independent reviews covered saves/serialization, PAK/native parsing, and CI/release provenance. The primary review integrated their findings and checked startup and repository state. This is a focused source and regression audit, not a penetration test or certification.

Six actionable findings remain: one P1, four P2, and one P3. Passing tests and authentic release assets do not establish that all malformed-input and UI paths are safe. No production code or repository settings were changed during this audit.

## Findings

### F1 — P1: Changing language bypasses stale-save protection

**Location:** `MCDSaveEdit/ViewModels/MainViewModel.cs:122–127`, especially line 125. Caller chain: `UI/Windows/MainWindow.xaml.cs:277–281` → `App.xaml.cs:230`.

Changing the UI language passes the existing in-memory profile to `setProfile`. That method captures a fresh snapshot of the file currently on disk, although the profile was not reread from those bytes. Saving now compares an old profile against the external writer's new snapshot and accepts the overwrite. This undermines the protection against accidentally replacing game progress.

**Reproduction:** Open a JSON profile with `xp=1`. Externally replace it with the same profile containing `xp=999`. A normal save correctly throws an `IOException`. Invoke `setProfile(path, originalProfile)`, as the language reload does, then save. The file contains `xp=1`. A temporary .NET harness linked the unchanged production MainViewModel and used production parser/atomic writer code; registry, UI holder, resources, and unrelated power calculation were stubbed. WPF interaction itself was source-traced, not executed on macOS. Recoverable backups reduce the impact but do not make this silent overwrite correct.

**Remediation:** Preserve the existing source snapshot during UI-only reloads. Establish a new baseline only when the model has actually been parsed from the corresponding disk content. Add a regression covering external edit → language reload → save rejection.

### F2 — P2: Untrusted package counts can allocate memory before validation

**Location:** `PakReader/PakReader/Parsers/Objects/FPackageFileSummary.cs:143–149`. Related direct count allocations appear in `Parsers/PropertyTagData/ArrayProperty.cs`, `SetProperty.cs`, `MapProperty.cs`, and `Parsers/Class/USoundWave.cs`.

The package summary reads a signed generation count and allocates `new FGenerationInfo[GenerationCount]` without checking remaining input or a practical allocation limit. The later package-table validation is too late. A tiny malformed summary can request a multi-gigabyte array before the parser reaches EOF. Similar property/audio collection counts remain unchecked. A user loading a malformed or modified local PAK can exhaust memory or terminate the editor; this is not evidence of code execution.

**Evidence:** Source-confirmed allocation order. For the legacy `-4` summary path, a 104-byte synthetic header can place a huge generation count at offset 100. Destructive memory-exhaustion tests were deliberately not executed.

**Remediation:** Validate every count before allocation, against remaining bytes, minimum serialized element size, and an aggregate memory budget. Reject negative values. Include bounded allocation regression cases for summary, collection, and audio readers.

### F3 — P2: Negative property size can rewind the parser indefinitely

**Location:** `PakReader/PakReader/Parsers/Class/UObject.cs:26–44`; size enters unchecked in `Parsers/Objects/FPropertyTag.cs:44`.

After parsing a property, UObject sets the reader position to `pos + Tag.Size` without requiring a nonnegative size or forward progress. A normal IntProperty tag has a 25-byte header; setting its size to `-25` can return the cursor to the same tag. The loop then parses it repeatedly and adds suffixed duplicate dictionary keys. File-entry size limits do not prevent this CPU/memory denial of service.

**Evidence:** Source-confirmed cursor arithmetic and duplicate-key behavior. An unbounded loop or memory exhaustion was not executed. This applies to malformed package content, not ordinary encrypted save JSON.

**Remediation:** Bound each property payload to its containing export, reject negative sizes and invalid end offsets before reading, require forward progress, and bound nesting/property counts. Add a regression that aborts after a bounded number of reader operations rather than allowing an uncontrolled loop.

### F4 — P2: Duplicate drops known item and enchantment metadata

**Location:** `MCDSaveEdit/Data/Profiles/Item.cs:65–79`, `Data/Profiles/Enchantment.cs:29–35`. UI trigger: `UI/Screens/ItemScreen.xaml.cs:476–480`.

Item.Copy preserves extension data but omits declared fields `Cloned`, `Modified`, and `TimesModified`; Enchantment.Copy omits `InvestedPoints`, including for the netherite enchantment. These omissions are separate from the explicitly intended removal of equipment slot and inventory index. Duplication therefore silently changes saved metadata, despite the improved unknown-field preservation.

**Reproduction:** A production Item with `TimesModified=7` and an enchantment with `InvestedPoints=12` produces null for both in Copy(). Confirmed in the temporary .NET harness. The original item remains intact. The exact gameplay consequences of each field were not verified in the game.

**Remediation:** Preserve all saved fields except explicitly documented placement/state transformations. Add a complete populated-item duplication round-trip regression, including nested enchantments and extension data.

### F5 — P2: Image preload failures leave startup permanently pending

**Location:** `MCDSaveEdit/App.xaml.cs:173–192`.

preloadImages creates a TaskCompletionSource, discards the task returned by Task.Run, and only completes the separate source on success. If any preload throws, the awaited completion source never finishes. Exceptions from package/texture decoding can reach these preloads through PakContentResolver → PakIndexExtensions; the surrounding startup catch never receives the worker failure. The application remains at its loading window instead of offering the limited-feature fallback.

**Evidence:** Source-confirmed task lifetime and exception path; no interactive WPF reproduction was performed on macOS. PakContentResolver.loadPakFilesAsync already uses the safer direct Task.Run pattern, but this separate preload stage retains the bug.

**Remediation:** Return/await the worker task directly, allowing the existing startup failure handler to run. Verify a throwing image resolver produces an error/fallback rather than a hanging task.

### F6 — P3: Native provenance exemption applies to nested executable names

**Location:** `scripts/validate-artifacts.py:119–120`.

The validator compares the lowercase basename to `mcdsaveedit.exe`, so any nested executable with that name is classified as an SDK-generated apphost. A synthetic same-architecture PE at `unexpected/MCDSaveEdit.exe` passed validation and received that origin label without a matching NuGet origin.

**Impact:** This is an assurance gap requiring prior control of trusted build output, not a demonstrated user exploit or evidence of a compromised release. No extra executable of that kind was observed in the published release. SHA-256 attestations would faithfully attest those bytes but would not correct a mistaken origin classification.

**Remediation:** Exempt only the exact root-relative application path. For stronger assurance, validate apphost generation against trusted SDK inputs. Add a nested-name regression.

## Verification and repository observations

- Production-source portable suite: **21 passed**, no failures/skips. Includes native texture tests on this host.
- Python validation/release suite: **31 passed**, no failures/skips, with the signed NuGet integration verifier enabled.
- Fresh locked restores of the portable project and full solution succeeded with NuGet audit enabled for all dependency severities. This checks reported advisories, not the absence of undiscovered dependency flaws.
- The release workflow [41483661936](https://github.com/compunaut/MCDSaveEdit/actions/runs/41483661936) passed Windows Debug/Release, x64/x86 native package smoke, packaging/attestation, publication, and verification jobs.
- Independent `gh release verify v1.0.0` succeeded. Its signed release statement binds commit `2ee88a735cff48b13c5f81583937bc23274c7da2` and all six expected assets. GitHub reports `immutable:true`, `draft:false`.
- GitHub reports `master` as **unprotected**. This is a repository governance gap, not proof of an exploit: the documented Final validation requirement is not enforced by classic branch protection. Configure branch protection/rulesets to require validation and deliberate review before future releases if that is the intended policy.
- `audit/IMPLEMENTATION-STATUS.md` predates hosted validation and release publication; its pending-state claims are historical and should not be used as the current status report.

## Limits and next work

No interactive Windows/gameplay/Wine or additional console compatibility validation was performed. The existing documented check-to-replace race with another filesystem writer remains; retained backups are the recovery mechanism. No claim of comprehensive native-library fuzzing, upstream binary malware analysis, Authenticode signing, or distribution-license compliance is made.

Prioritize F1 first, then parser count/size bounds and preload propagation, then duplication preservation and the provenance exemption. Add regression coverage for each fix, rerun the existing Windows/native gates, and publish a new version after validation. The immutable v1.0.0 assets should remain unchanged.
