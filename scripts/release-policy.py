#!/usr/bin/env python3
"""Fail closed unless GitHub confirms native immutable release attestations."""
import json
import os
import urllib.error
import urllib.request

REPOSITORY = 'compunaut/MCDSaveEdit'


def request(path, allow_missing=False):
    req = urllib.request.Request('https://api.github.com/repos/' + REPOSITORY + path,
        headers={'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2026-03-10',
                 'Authorization': 'Bearer ' + os.environ['GH_TOKEN'], 'User-Agent': 'MCDSaveEdit-release'})
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        if error.code == 404 and allow_missing:
            return None
        raise RuntimeError('Cannot verify release policy (HTTP %s). Enable release immutability in '
            'repository Settings > General > Releases and configure RELEASE_POLICY_READ_TOKEN '
            'with Administration:read for this fork only; see release/WORKFLOW.md.' % error.code) from None


def check_policy(policy, existing):
    if policy.get('enabled') is not True:
        raise ValueError('Repository release immutability is not enabled; no release may be published.')
    if existing is not None:
        raise ValueError('v1.0.0 already exists (including drafts). Never overwrite an existing release.')


if __name__ == '__main__':
    if os.environ.get('GITHUB_REPOSITORY') != REPOSITORY:
        raise SystemExit('Release destination must be the personal compunaut/MCDSaveEdit fork.')
    check_policy(request('/immutable-releases'), request('/releases/tags/v1.0.0', allow_missing=True))
    print('GitHub confirms immutable releases are enabled; v1.0.0 does not exist.')
