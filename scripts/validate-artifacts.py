#!/usr/bin/env python3
"""Validate publish output against restored NuGet native assets; write evidence.

Framework-dependent, directory publishes only. This verifies provenance relative
to locked NuGet bytes, not that those upstream binaries are inherently safe.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import zipfile


def digest(path, algorithm="sha256"):
    hasher = hashlib.new(algorithm)
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(block)
    return hasher


def pe_info(path):
    """Return PE machine and whether the CLR directory identifies managed code."""
    data = path.read_bytes()
    if len(data) < 64 or data[:2] != b"MZ":
        raise ValueError(f"Invalid PE file: {path}")
    offset = struct.unpack_from("<I", data, 60)[0]
    if offset + 24 > len(data) or data[offset:offset + 4] != b"PE\0\0":
        raise ValueError(f"Invalid PE header: {path}")
    machine, = struct.unpack_from("<H", data, offset + 4)
    optional = offset + 24
    magic, = struct.unpack_from("<H", data, optional)
    if magic not in (0x10b, 0x20b):
        raise ValueError(f"Unsupported PE optional header: {path}")
    directories = optional + (96 if magic == 0x10b else 112)
    if directories + 15 * 8 > len(data):
        raise ValueError(f"Truncated PE directories: {path}")
    clr_rva, clr_size = struct.unpack_from("<II", data, directories + 14 * 8)
    return machine, bool(clr_rva and clr_size)


def validate(output, assets_path, rid, evidence, integrity_tool=None, dotnet_host=None):
    output = output.resolve()
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    targets = [v for k, v in assets["targets"].items() if k.endswith("/" + rid)]
    if len(targets) != 1:
        raise ValueError(f"Expected exactly one restored target for {rid}")
    folders = [Path(p) for p in assets["packageFolders"]]
    origins, packages = {}, []
    for identity, entries in targets[0].items():
        library = assets["libraries"][identity]
        if library.get("type") != "package":
            continue
        package_path = next((root / library["path"] for root in folders
                             if (root / library["path"]).is_dir()), None)
        if package_path is None:
            raise ValueError(f"Missing restored package {identity}")
        archives = list(package_path.glob("*.nupkg"))
        if len(archives) != 1:
            raise ValueError(f"Missing or ambiguous package archive: {identity}")
        package_hash = base64.b64encode(digest(archives[0], "sha512").digest()).decode()
        content_hash = library.get("sha512", "").removeprefix("sha512-")
        if package_hash != content_hash:
            if integrity_tool is None:
                raise ValueError(f"NuGet archive hash differs from resolved assets: {identity}; "
                                 "signed packages require the SDK NuGet integrity tool")
            host = dotnet_host or os.environ.get("DOTNET_HOST_PATH", "dotnet")
            verified = subprocess.run([host, str(integrity_tool), str(archives[0]), content_hash],
                                      capture_output=True, text=True, check=False)
            if verified.returncode != 0:
                raise ValueError(f"NuGet content integrity failure: {identity}: {verified.stderr.strip()}")
            result = json.loads(verified.stdout)
            if result.get("contentHash") != content_hash:
                raise ValueError(f"NuGet verifier returned an unexpected hash: {identity}")
        packages.append({"package": identity, "contentHash": content_hash, "archiveSha512": package_hash})
        native = list(entries.get("native", {}))
        native += [p for p, info in entries.get("runtimeTargets", {}).items()
                   if info.get("assetType") == "native" and info.get("rid") == rid]
        for name in native:
            source = package_path / name
            if source.name == "_._":
                continue
            if not source.is_file():
                raise ValueError(f"Missing package native asset: {source}")
            with zipfile.ZipFile(archives[0]) as archive:
                archive_hash = hashlib.sha256(archive.read(name)).hexdigest()
            if digest(source).hexdigest() != archive_hash:
                raise ValueError(f"Extracted native asset differs from NuGet archive: {source}")
            origins.setdefault(source.name.lower(), []).append(
                {"package": identity, "asset": name, "sha256": archive_hash})
    files, seen_native = [], set()
    expected_machine = {"win-x64": 0x8664, "win-x86": 0x14c}[rid]
    if not (output / "MCDSaveEdit.exe").is_file():
        raise ValueError("Missing application executable")
    if not (output / "MCDSaveEdit.runtimeconfig.json").is_file():
        raise ValueError("Missing runtime configuration; directory publish required")
    for path in sorted(output.rglob("*")):
        if path.is_symlink():
            raise ValueError(f"Symlinks are not permitted in publish output: {path}")
        if not path.is_file():
            continue
        relative = path.relative_to(output).as_posix()
        lower = path.name.lower()
        if lower.endswith((".so", ".dylib")) or ".so." in lower:
            raise ValueError(f"Non-Windows native library in Windows publish output: {relative}")
        if any(term in lower for term in ("sqlite", "gameanalytics", "costura")):
            raise ValueError(f"Removed legacy component in publish output: {relative}")
        record = {"path": relative, "sha256": digest(path).hexdigest(), "size": path.stat().st_size}
        if path.suffix.lower() in (".dll", ".exe"):
            machine, managed = pe_info(path)
            if not managed:
                if machine != expected_machine:
                    raise ValueError(f"Wrong native architecture: {relative}")
                if relative == "MCDSaveEdit.exe":
                    record["origin"] = "SDK-generated apphost (not a NuGet native library)"
                else:
                    matches = [p for p in origins.get(lower, []) if p["sha256"] == record["sha256"]]
                    if not matches:
                        raise ValueError(f"Native library has no matching restored NuGet origin: {relative}")
                    record["nugetOrigins"] = matches
                    seen_native.add(lower)
        files.append(record)
    if "libskiasharp.dll" not in seen_native:
        raise ValueError("Expected Skia native asset is missing")
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "artifact-inventory.json").write_text(json.dumps(
        {"runtime": rid, "packages": packages, "files": files}, indent=2) + "\n", encoding="utf-8")
    (evidence / "SHA256SUMS").write_text("".join(
        f"{item['sha256']}  {item['path']}\n" for item in files), encoding="utf-8")
    print(f"Verified {len(files)} files and native NuGet provenance for {rid}.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--assets", required=True, type=Path)
    parser.add_argument("--rid", required=True, choices=("win-x64", "win-x86"))
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--integrity-tool", type=Path,
                        help="Built SDK PackageIntegrity.dll, required for signed NuGet packages")
    parser.add_argument("--dotnet", help="dotnet executable; defaults to DOTNET_HOST_PATH or PATH")
    args = parser.parse_args()
    try:
        validate(args.output, args.assets, args.rid, args.evidence, args.integrity_tool, args.dotnet)
    except (ValueError, KeyError, OSError, struct.error) as exc:
        sys.exit(f"Artifact validation failed: {exc}")
