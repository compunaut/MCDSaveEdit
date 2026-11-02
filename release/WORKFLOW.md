# Publishing v1.0.2 with attestations

The workflow `.github/workflows/release.yml` runs manually from `master` or on the exact `v1.0.2` tag. It only publishes to `compunaut/MCDSaveEdit`, requires the source commit to be on `master`, and checks the application version is `1.0.2.0`. All standard validation runs against that same commit through the reusable validation workflow.

Before publication, an administrator must enable **Settings → General → Releases → Enable release immutability**. This is required for GitHub's native release attestation binding the tag, commit, and all release assets. It cannot be replaced by a build-provenance attestation.

The immutable-policy REST endpoint requires **Administration: read**, which the normal Actions `GITHUB_TOKEN` cannot request. Configure repository secret `RELEASE_POLICY_READ_TOKEN` with a fine-grained token limited to this repository and Administration: read only (no administration write). It is used only for the two immutable-policy checks and is never passed to builds or package signing. The workflow falls back to `GITHUB_TOKEN`, but if GitHub refuses the policy read, the workflow stops without creating a release. Do not grant a broader token to bypass this check; supply only the documented read permission.

Use **Actions → Release v1.0.2 → Run workflow → master**. Alternatively:

```sh
gh workflow run release.yml --repo compunaut/MCDSaveEdit --ref master
```

Validation runs Windows Debug/Release tests, portable tests, vulnerability checks, package-integrity tests, native provenance and actual x64/x86 executable smoke checks. Packaging consumes only the current run's named validated artifacts, rechecks every binary hash, and creates deterministic architecture ZIPs with notices. The matching complete source ZIP is included. No code is rebuilt after validation.

Only package jobs receive OIDC and attestations-write permissions. SHA-pinned `actions/attest` signs the finalized ZIPs with SLSA build provenance. Sigstore bundles are downloadable release assets. The publishing job receives contents-write and attestations-read, verifies each ZIP's repository, signer workflow and source digest, generates `SHA256SUMS`, creates a draft, uploads all assets, confirms the exact draft asset set, and publishes once. It then verifies GitHub's native release attestation and every asset via `gh release verify` / `verify-asset`.

Existing releases or drafts are rejected and never overwritten. If upload/publication fails after draft creation, inspect and resolve the draft deliberately before retrying; the workflow does not delete evidence or silently clobber assets. Repository policy is checked again immediately before publication. Do not disable immutable releases while a release run is active.

Verify downloads:

```sh
gh release verify v1.0.2 --repo compunaut/MCDSaveEdit
gh release verify-asset v1.0.2 MCDSaveEdit-v1.0.2-win-x64.zip --repo compunaut/MCDSaveEdit
gh attestation verify MCDSaveEdit-v1.0.2-win-x64.zip \
  --repo compunaut/MCDSaveEdit \
  --signer-workflow compunaut/MCDSaveEdit/.github/workflows/release.yml
```

Build provenance and release attestations are distinct from Windows Authenticode signing. The ZIPs require the matching serviced .NET 10 Desktop Runtime; manual gameplay/console/Wine compatibility limits remain stated in the release notes. Attestations are not evidence of those manual checks.

Primary references: [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations/use-artifact-attestations), [immutable releases and automatic release attestations](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases), [immutable-policy API permissions](https://docs.github.com/en/rest/repos/repos#check-if-immutable-releases-are-enabled-for-a-repository).
