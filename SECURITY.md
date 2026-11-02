# Personal fork security and validation

This repository is a personal fork. Validation CI builds and retains short-lived review artifacts. The dedicated
[release workflow](release/WORKFLOW.md) publishes only to this fork, with narrowly
scoped signing and publishing permissions and mandatory immutable releases.
No workflow pushes commits or opens upstream PRs.
Report suspected vulnerabilities privately to the personal fork's maintainer.
Do not attach private save files, account information, or game encryption keys
to public issues or CI logs.

## Required validation gate

Configure the fork's branch protection to require **Final validation** before
merging. It aggregates portable safety/CI-gate tests, both Windows build/test
configurations, and both published architectures. A failed, cancelled, or skipped
prerequisite fails that gate.
The workflow uses read-only repository permissions, ephemeral GitHub-hosted
runners, SHA-pinned official actions, and checkout without persisted credentials.
It never uses `pull_request_target`, untrusted privileged follow-up workflows,
repository secrets, OIDC credentials, or shared dependency caches. Dependabot
proposes package and action updates for review; nothing auto-merges.

NuGet restore runs in locked mode and treats audit lookup failures and every
reported vulnerability severity as errors. Direct and transitive dependency
reports are also checked explicitly. An audit service failure is a failed check,
not evidence that packages are safe. Update lock files intentionally when
reviewing dependency updates; never disable locked restore to fix CI.

## Native and package evidence

The workflow publishes framework-dependent directory packages for `win-x64` and
`win-x86`. Keep the full directory together; the matching .NET Desktop Runtime
must be installed separately and kept up to date. Trimming and single-file native
embedding would hide inputs from the current validator and are not supported.
Each package job installs the matching SDK architecture in an isolated directory
and runs the published executable's `--verify-native <reportPath>` check with a
30-second limit. This exercises real pixel decoding/PNG encoding, verifies the
native library was loaded from the publish directory, and checks the reported
process architecture. Its JSON report is retained with artifact evidence.

`scripts/validate-artifacts.py` checks native PE architecture, requires the Skia
native library, rejects legacy SQLite/analytics/Costura output, and fails on
non-Windows native libraries or native DLLs without a matching runtime asset in
restored NuGet metadata. For
each package it verifies NuGet's canonical content hash against resolved package
metadata. Signed packages use the NuGet verifier shipped in the pinned SDK to
check signed archive integrity and its canonical content hash; a raw ZIP hash
cannot be substituted because signing changes archive bytes. For each native
asset it also matches extracted bytes to the verified archive.
Locked restore ties this metadata to checked-in package hashes. The SDK-generated
application host is identified separately; it is not claimed as a NuGet library.
The verifier accepts `--dotnet /absolute/path/to/dotnet`, or `DOTNET_HOST_PATH`,
when the SDK host is not on `PATH`.

Each validated artifact includes `SHA256SUMS`, a file/package/native provenance
inventory, and NuGet dependency and vulnerability JSON reports. These provide
review evidence, not code-signing, malware analysis, an independent upstream
binary audit, or a standards-compliant SBOM. Do not distribute a package merely
because a dependency scanner is clean. Review changes, run the full validation,
then manually smoke-test UI/save compatibility with disposable save copies on
Windows. Game-installation integration tests remain a separate manual check.

The pinned action commits were checked against official `actions/*` repositories
using `git ls-remote`: checkout v7.0.1, setup-dotnet v6.0.0, and upload-artifact
v7.0.2. A future update must preserve immutable commit pins.
