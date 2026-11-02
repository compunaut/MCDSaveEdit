#!/usr/bin/env python3
"""Verify finalized assets, publish once, and verify GitHub's release attestation."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

REPOSITORY = 'compunaut/MCDSaveEdit'
TAG = 'v1.0.2'
EXPECTED = {
    'MCDSaveEdit-v1.0.2-win-x64.zip', 'MCDSaveEdit-v1.0.2-win-x86.zip',
    'MCDSaveEdit-v1.0.2-source.zip',
    'MCDSaveEdit-v1.0.2-win-x64.sigstore.json', 'MCDSaveEdit-v1.0.2-win-x86.sigstore.json',
}


def release_assets(directory):
    paths = list(directory.iterdir())
    if {p.name for p in paths} != EXPECTED or any(not p.is_file() or p.is_symlink() for p in paths):
        raise ValueError('Release assets are missing, unexpected, or unsafe.')
    return sorted(paths)


def run(*args, capture=False, retry=False):
    for attempt in range(6 if retry else 1):
        result = subprocess.run(['gh', *args], text=True, stdout=subprocess.PIPE if capture else None)
        if result.returncode == 0:
            return result.stdout
        if not retry or attempt == 5:
            raise subprocess.CalledProcessError(result.returncode, result.args)
        time.sleep(5)


def publish(directory=Path('dist')):
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY:
        raise ValueError('Unexpected release repository')
    commit = os.environ['GITHUB_SHA']
    if len(commit) != 40 or any(c not in '0123456789abcdef' for c in commit):
        raise ValueError('Invalid release commit')
    assets = release_assets(directory)
    evidence = Path('release-verification')
    evidence.mkdir(exist_ok=True)
    for path in assets:
        if path.suffix == '.zip':
            verification = run('attestation', 'verify', str(path), '--repo', REPOSITORY,
                '--source-digest', commit, '--signer-workflow', REPOSITORY + '/.github/workflows/release.yml',
                '--deny-self-hosted-runners', '--format', 'json', capture=True, retry=True)
            (evidence / (path.name + '.provenance.json')).write_text(verification)
    sums = directory / 'SHA256SUMS'
    sums.write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + p.name + '\n' for p in assets))
    assets.append(sums)
    # Listing with the publishing token includes drafts, unlike public tag lookup.
    pages = json.loads(run('api', 'repos/' + REPOSITORY + '/releases?per_page=100',
                           '--paginate', '--slurp', capture=True))
    if any(release.get('tag_name') == TAG for page in pages for release in page):
        raise ValueError('Existing v1.0.2 release or draft must be resolved deliberately.')
    # No clobber or update path: any existing release/draft aborts instead.
    created = json.loads(run('api', '--method', 'POST', 'repos/' + REPOSITORY + '/releases',
        '--raw-field', 'tag_name=' + TAG, '--raw-field', 'target_commitish=' + commit,
        '--raw-field', 'name=MCDSaveEdit v1.0.2', '--field', 'draft=true',
        '--raw-field', 'body=' + Path('release/v1.0.2.md').read_text(), capture=True))
    release_id = created['id']
    run('release', 'upload', TAG, '--repo', REPOSITORY, *map(str, assets))
    # Confirm the complete draft before the irreversible immutable publication.
    draft = json.loads(run('api', 'repos/' + REPOSITORY + '/releases/' + str(release_id), capture=True))
    if (not draft.get('draft') or draft.get('target_commitish') != commit
            or {a['name'] for a in draft['assets']} != {p.name for p in assets}):
        raise ValueError('Draft asset set differs from the verified release inputs; draft left unpublished.')
    expected_digests = {p.name: 'sha256:' + hashlib.sha256(p.read_bytes()).hexdigest() for p in assets}
    if {a['name']: a.get('digest') for a in draft['assets']} != expected_digests:
        raise ValueError('Remote draft asset digests differ; draft left unpublished.')
    # A pre-existing tag overrides --target. Resolve annotated tags too, and
    # refuse to publish different source than the attested package commit.
    ref_result = subprocess.run(['gh', 'api', 'repos/' + REPOSITORY + '/git/ref/tags/' + TAG],
                                capture_output=True, text=True)
    if ref_result.returncode == 0:
        obj = json.loads(ref_result.stdout)['object']
        for _ in range(8):
            if obj['type'] == 'commit':
                break
            if obj['type'] != 'tag':
                raise ValueError('Unexpected release tag object type.')
            obj = json.loads(run('api', 'repos/' + REPOSITORY + '/git/tags/' + obj['sha'], capture=True))['object']
        if obj['type'] != 'commit' or obj['sha'] != commit:
            raise ValueError('Release tag differs from the attested source; draft left unpublished.')
    elif 'HTTP 404' not in ref_result.stderr:
        raise RuntimeError('Cannot verify the release tag; draft left unpublished.')
    run('release', 'edit', TAG, '--repo', REPOSITORY, '--draft=false', '--latest')
    verification = run('release', 'verify', TAG, '--repo', REPOSITORY, '--format', 'json', capture=True, retry=True)
    (evidence / 'release-attestation-verification.json').write_text(verification)
    for path in assets:
        verified = run('release', 'verify-asset', TAG, str(path), '--repo', REPOSITORY,
                       '--format', 'json', capture=True, retry=True)
        (evidence / (path.name + '.release-verification.json')).write_text(verified)
    print('Published immutable v1.0.2 with verified build and release attestations.')


if __name__ == '__main__':
    publish()
