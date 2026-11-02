#!/usr/bin/env python3
"""Package only hash-verified validation output; never rebuild release binaries."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import zipfile


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def zip_files(destination, files):
    with zipfile.ZipFile(destination, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(files.items()):
            info = zipfile.ZipInfo(name, (2000, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, data)


def package(validated, source, output, rid):
    if rid not in ('win-x64', 'win-x86'):
        raise ValueError('Unsupported release architecture')
    inventory = json.loads((validated / 'evidence/artifact-inventory.json').read_text())
    smoke = json.loads((validated / 'evidence/native-smoke.json').read_text())
    if inventory['runtime'] != rid or smoke.get('success') is not True or smoke.get('architecture', '').lower() != rid[4:]:
        raise ValueError('Missing or mismatched Windows native validation')
    publish = validated / 'publish'
    files = {}
    for entry in inventory['files']:
        name = entry['path']
        p = PurePosixPath(name)
        if p.is_absolute() or '..' in p.parts or chr(92) in name or ':' in name or name in files:
            raise ValueError('Unsafe or duplicate inventory path')
        path = publish / name
        if path.is_symlink() or not path.is_file() or sha256(path) != entry['sha256']:
            raise ValueError('Published file differs from validated inventory: ' + name)
        files[name] = path.read_bytes()
    actual = {p.relative_to(publish).as_posix() for p in publish.rglob('*') if p.is_file()}
    if actual != set(files) or any(p.is_symlink() for p in publish.rglob('*')):
        raise ValueError('Unvalidated files in publish directory')
    if not {'MCDSaveEdit.exe', 'MCDSaveEdit.dll', 'libSkiaSharp.dll'}.issubset(files):
        raise ValueError('Incomplete application package')
    for path in sorted((validated / 'evidence').glob('*')):
        if path.is_symlink() or not path.is_file():
            raise ValueError('Unexpected evidence entry')
        files['validation/' + path.name] = path.read_bytes()
    for name, path in {
        'LICENSE': 'LICENSE',
        'licenses/DungeonTools-AGPL-3.0.txt': 'DungeonTools/DungeonTools/LICENSE',
        'licenses/PakReader-MIT.txt': 'PakReader/PakReader/LICENSE',
        'THIRD-PARTY-NOTICES.md': 'VENDORED.md',
        'UPSTREAM-CREDITS.md': 'README.md',
        'README.txt': 'release/README.txt',
    }.items():
        files[name] = (source / path).read_bytes()
    output.mkdir(parents=True, exist_ok=True)
    archive = output / ('MCDSaveEdit-v1.0.1-' + rid + '.zip')
    zip_files(archive, files)
    print(archive)
    return archive


if __name__ == '__main__':
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('--validated', required=True, type=Path)
    cli.add_argument('--source', type=Path, default=Path('.'))
    cli.add_argument('--output', type=Path, default=Path('dist'))
    cli.add_argument('--rid', required=True, choices=('win-x64', 'win-x86'))
    args = cli.parse_args()
    package(args.validated, args.source, args.output, args.rid)
