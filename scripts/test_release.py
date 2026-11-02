"""Release packaging and publication failure checks with no live mutations."""
import copy
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

packager = load('package-release')
policy = load('release-policy')
publisher = load('publish-release')


class ReleasePackageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.validated = self.root / 'validated'
        self.source = self.root / 'source'
        self.output = self.root / 'output'
        publish = self.validated / 'publish'
        evidence = self.validated / 'evidence'
        publish.mkdir(parents=True)
        evidence.mkdir()
        entries = []
        for name in ('MCDSaveEdit.exe', 'MCDSaveEdit.dll', 'libSkiaSharp.dll'):
            data = ('verified bytes ' + name).encode()
            (publish / name).write_bytes(data)
            entries.append({'path': name, 'sha256': hashlib.sha256(data).hexdigest()})
        (evidence / 'artifact-inventory.json').write_text(json.dumps({'runtime': 'win-x64', 'files': entries}))
        (evidence / 'native-smoke.json').write_text(json.dumps({'success': True, 'architecture': 'X64'}))
        for name in ('LICENSE', 'DungeonTools/DungeonTools/LICENSE', 'PakReader/PakReader/LICENSE',
                     'VENDORED.md', 'README.md', 'release/README.txt'):
            path = self.source / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('notice for ' + name)

    def package(self):
        return packager.package(self.validated, self.source, self.output, 'win-x64')

    def test_release_contains_exact_validated_binaries_and_licenses(self):
        archive = self.package()
        with zipfile.ZipFile(archive) as z:
            self.assertEqual(z.read('MCDSaveEdit.exe'), b'verified bytes MCDSaveEdit.exe')
            self.assertIn('licenses/DungeonTools-AGPL-3.0.txt', z.namelist())
            self.assertIn('validation/native-smoke.json', z.namelist())

    def test_tampered_binary_is_rejected_before_zip_creation(self):
        (self.validated / 'publish/MCDSaveEdit.exe').write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'differs'):
            self.package()
        self.assertFalse(self.output.exists())

    def test_extra_unvalidated_file_is_rejected(self):
        (self.validated / 'publish/extra.dll').write_bytes(b'extra')
        with self.assertRaisesRegex(ValueError, 'Unvalidated'):
            self.package()

    def test_wrong_architecture_or_failed_smoke_is_rejected(self):
        for value in ({'success': False, 'architecture': 'X64'}, {'success': True, 'architecture': 'X86'}):
            (self.validated / 'evidence/native-smoke.json').write_text(json.dumps(value))
            with self.assertRaisesRegex(ValueError, 'native validation'):
                self.package()

    def test_inventory_traversal_is_rejected(self):
        p = self.validated / 'evidence/artifact-inventory.json'
        data = json.loads(p.read_text())
        data['files'][0]['path'] = '../escape.exe'
        p.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'Unsafe'):
            self.package()

    def test_archives_are_deterministic(self):
        a = self.package().read_bytes()
        self.output = self.root / 'second-output'
        self.assertEqual(a, self.package().read_bytes())


class ReleasePolicyTests(unittest.TestCase):
    def test_native_immutability_required(self):
        for value in ({}, {'enabled': False}, {'enabled': 'true'}):
            with self.assertRaises(ValueError):
                policy.check_policy(value, None)
        policy.check_policy({'enabled': True}, None)

    def test_existing_draft_or_release_is_never_overwritten(self):
        for existing in ({'draft': True}, {'draft': False}):
            with self.assertRaises(ValueError):
                policy.check_policy({'enabled': True}, existing)

    def test_provenance_failure_cannot_create_release(self):
        with tempfile.TemporaryDirectory() as tmp:
            dist = Path(tmp)
            for name in publisher.EXPECTED:
                (dist / name).write_bytes(b'archive')
            with patch.dict(os.environ, {'GITHUB_REPOSITORY': publisher.REPOSITORY, 'GITHUB_SHA': 'a' * 40}):
                with patch.object(publisher, 'run', side_effect=RuntimeError('bad signature')) as run:
                    with self.assertRaises(RuntimeError):
                        publisher.publish(dist)
                    self.assertTrue(all(call.args[0] == 'attestation' for call in run.call_args_list))

    def test_missing_asset_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ValueError):
                publisher.release_assets(Path(tmp))


class ReleasePublisherTests(unittest.TestCase):
    """Exercise publication ordering and irreversible-operation rejection paths."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        original_directory = Path.cwd()
        self.addCleanup(os.chdir, original_directory)
        self.root = Path(self.tmp.name)
        os.chdir(self.root)
        self.dist = self.root / 'dist'
        self.dist.mkdir()
        for name in publisher.EXPECTED:
            (self.dist / name).write_bytes(('verified release asset ' + name).encode())
        (self.root / 'release').mkdir()
        (self.root / 'release/v1.0.1.md').write_text('Reviewed release notes.\n')
        self.commit = 'a' * 40
        self.prefix = 'repos/' + publisher.REPOSITORY
        self.events = []
        self.digest_mismatch = False
        self.tag_object = {'type': 'commit', 'sha': self.commit}
        self.annotated_targets = {}
        self.existing_pages = [[]]
        self.environment = patch.dict(os.environ, {
            'GITHUB_REPOSITORY': publisher.REPOSITORY, 'GITHUB_SHA': self.commit,
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.command_patch = patch.object(publisher, 'run', side_effect=self.fake_gh)
        self.command = self.command_patch.start()
        self.addCleanup(self.command_patch.stop)
        self.ref_patch = patch.object(publisher.subprocess, 'run', side_effect=self.fake_ref)
        self.ref_command = self.ref_patch.start()
        self.addCleanup(self.ref_patch.stop)

    def fake_gh(self, *args, **kwargs):
        self.events.append(args)
        if args[:2] == ('attestation', 'verify'):
            return json.dumps([{'verified': True}])
        if args[:2] == ('api', self.prefix + '/releases?per_page=100'):
            self.assertIn('--paginate', args)
            self.assertIn('--slurp', args)
            return json.dumps(self.existing_pages)
        if args[:4] == ('api', '--method', 'POST', self.prefix + '/releases'):
            self.assertIn('draft=true', args)
            self.assertIn('target_commitish=' + self.commit, args)
            return json.dumps({'id': 42, 'draft': True})
        if args[:2] == ('release', 'upload'):
            self.assertNotIn('--clobber', args)
            return ''
        if args == ('api', self.prefix + '/releases/42'):
            assets = [{'name': path.name, 'digest': 'sha256:' + hashlib.sha256(path.read_bytes()).hexdigest()}
                      for path in sorted(self.dist.iterdir())]
            if self.digest_mismatch:
                assets[0]['digest'] = 'sha256:' + '0' * 64
            return json.dumps({'id': 42, 'draft': True, 'target_commitish': self.commit, 'assets': assets})
        if args[0] == 'api' and args[1].startswith(self.prefix + '/git/tags/'):
            sha = args[1].rsplit('/', 1)[-1]
            return json.dumps({'object': self.annotated_targets[sha]})
        if args[:2] == ('release', 'edit'):
            self.assertIn('--draft=false', args)
            return ''
        if args[:2] in (('release', 'verify'), ('release', 'verify-asset')):
            return json.dumps({'verified': True, 'tag': publisher.TAG})
        self.fail('Unexpected GitHub CLI invocation: ' + repr(args))

    def fake_ref(self, args, **kwargs):
        expected = ['gh', 'api', self.prefix + '/git/ref/tags/' + publisher.TAG]
        self.assertEqual(args, expected)
        self.events.append(('resolved-tag',))
        return subprocess.CompletedProcess(args, 0, json.dumps({'object': self.tag_object}), '')

    def publish(self):
        with contextlib.redirect_stdout(io.StringIO()):
            publisher.publish(self.dist)

    def publication_calls(self):
        return [event for event in self.events if event[:2] == ('release', 'edit')]

    def test_successful_publish_attaches_every_asset_then_verifies_release_and_assets(self):
        self.publish()
        self.assertEqual(len(self.publication_calls()), 1)
        provenance = [event for event in self.events if event[:2] == ('attestation', 'verify')]
        self.assertEqual(len(provenance), 3)
        for event in provenance:
            self.assertEqual(event[event.index('--source-digest') + 1], self.commit)
            self.assertIn('--signer-workflow', event)
            self.assertIn('--deny-self-hosted-runners', event)
        positions = {name: next(i for i, event in enumerate(self.events) if event[:2] == prefix)
                     for name, prefix in [('upload', ('release', 'upload')),
                                          ('publish', ('release', 'edit')),
                                          ('verify', ('release', 'verify'))]}
        self.assertLess(positions['upload'], positions['publish'])
        self.assertLess(self.events.index(('resolved-tag',)), positions['publish'])
        self.assertLess(positions['publish'], positions['verify'])
        checksums = (self.dist / 'SHA256SUMS').read_text()
        for name in publisher.EXPECTED:
            digest = hashlib.sha256((self.dist / name).read_bytes()).hexdigest()
            self.assertIn(digest + '  ' + name + '\n', checksums)
        verified_assets = [event for event in self.events if event[:2] == ('release', 'verify-asset')]
        self.assertEqual({Path(event[3]).name for event in verified_assets}, publisher.EXPECTED | {'SHA256SUMS'})
        evidence = self.root / 'release-verification'
        self.assertEqual(len(list(evidence.glob('*.provenance.json'))), 3)
        self.assertTrue((evidence / 'release-attestation-verification.json').is_file())
        self.assertEqual(len(list(evidence.glob('*.release-verification.json'))), 6)

    def test_uploaded_digest_mismatch_leaves_draft_unpublished(self):
        self.digest_mismatch = True
        with self.assertRaisesRegex(ValueError, 'Remote draft asset digests differ'):
            self.publish()
        self.assertTrue(any(event[:2] == ('release', 'upload') for event in self.events))
        self.assertEqual(self.publication_calls(), [])
        self.ref_command.assert_not_called()
        self.assertFalse(any(event[:2] == ('release', 'verify') for event in self.events))

    def test_wrong_existing_tag_leaves_draft_unpublished(self):
        self.tag_object = {'type': 'commit', 'sha': 'b' * 40}
        with self.assertRaisesRegex(ValueError, 'Release tag differs from the attested source'):
            self.publish()
        self.ref_command.assert_called_once()
        self.assertEqual(self.publication_calls(), [])
        self.assertFalse(any(event[:2] == ('release', 'verify') for event in self.events))

    def test_annotated_tag_is_resolved_before_publication(self):
        tag_sha = 'c' * 40
        self.tag_object = {'type': 'tag', 'sha': tag_sha}
        self.annotated_targets[tag_sha] = {'type': 'commit', 'sha': self.commit}
        self.publish()
        tag_read = ('api', self.prefix + '/git/tags/' + tag_sha)
        self.assertIn(tag_read, self.events)
        self.assertLess(self.events.index(tag_read), self.events.index(self.publication_calls()[0]))

    def test_existing_draft_on_later_page_prevents_creation_or_upload(self):
        self.existing_pages = [[{'tag_name': 'v0.9.0', 'draft': False}],
                               [{'tag_name': publisher.TAG, 'draft': True}]]
        with self.assertRaisesRegex(ValueError, 'Existing v1.0.1 release or draft'):
            self.publish()
        self.assertFalse(any(event[:3] == ('api', '--method', 'POST') for event in self.events))
        self.assertFalse(any(event[:2] == ('release', 'upload') for event in self.events))
        self.assertEqual(self.publication_calls(), [])


if __name__ == '__main__':
    unittest.main()
