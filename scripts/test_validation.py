"""Exercise fail-closed CI gates against tampered packages and incomplete audits."""
import base64
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
import zipfile


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


artifacts = load("validate-artifacts")
vulnerabilities = load("check-vulnerabilities")


def native_pe(machine=0x8664):
    data = bytearray(512)
    data[:2] = b"MZ"
    struct.pack_into("<I", data, 60, 64)
    data[64:68] = b"PE\0\0"
    struct.pack_into("<H", data, 68, machine)
    struct.pack_into("<H", data, 88, 0x20b if machine == 0x8664 else 0x10b)
    return bytes(data)


class ArtifactGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.output = self.root / "publish"
        self.output.mkdir()
        self.evidence = self.root / "evidence"
        self.native_name = "runtimes/win-x64/native/libSkiaSharp.dll"
        self.package = self.root / "packages/skia/1.0"
        self.native = self.package / self.native_name
        self.native.parent.mkdir(parents=True)
        self.native.write_bytes(native_pe())
        archive = self.package / "skia.1.0.nupkg"
        with zipfile.ZipFile(archive, "w") as stream:
            stream.write(self.native, self.native_name)
        package_hash = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
        self.assets = self.root / "project.assets.json"
        self.assets.write_text(json.dumps({
            "targets": {"net10.0-windows/win-x64": {"skia/1.0": {
                "native": {self.native_name: {}}}}},
            "libraries": {"skia/1.0": {"type": "package", "path": "skia/1.0", "sha512": package_hash}},
            "packageFolders": {str(self.root / "packages"): {}},
        }))
        (self.output / "MCDSaveEdit.exe").write_bytes(native_pe())
        (self.output / "MCDSaveEdit.runtimeconfig.json").write_text("{}")
        (self.output / "libSkiaSharp.dll").write_bytes(native_pe())

    def validate(self):
        with contextlib.redirect_stdout(io.StringIO()):
            artifacts.validate(self.output, self.assets, "win-x64", self.evidence)

    def test_valid_output_records_native_origin_and_checksums(self):
        self.validate()
        evidence = json.loads((self.evidence / "artifact-inventory.json").read_text())
        native = next(f for f in evidence["files"] if f["path"] == "libSkiaSharp.dll")
        self.assertEqual(native["nugetOrigins"][0]["package"], "skia/1.0")
        self.assertIn("libSkiaSharp.dll", (self.evidence / "SHA256SUMS").read_text())

    def test_wrong_architecture_rejected(self):
        (self.output / "libSkiaSharp.dll").write_bytes(native_pe(0x14c))
        with self.assertRaisesRegex(ValueError, "architecture"):
            self.validate()

    def test_unknown_native_rejected(self):
        (self.output / "injected.dll").write_bytes(native_pe())
        with self.assertRaisesRegex(ValueError, "no matching"):
            self.validate()

    def test_nested_application_name_requires_native_provenance(self):
        nested = self.output / "unexpected" / "MCDSaveEdit.exe"
        nested.parent.mkdir()
        nested.write_bytes(native_pe() + b"not-an-sdk-generated-apphost")
        with self.assertRaisesRegex(ValueError, "no matching restored NuGet origin: unexpected/MCDSaveEdit.exe"):
            self.validate()
        self.assertFalse((self.evidence / "artifact-inventory.json").exists())

    def test_publish_tampering_rejected(self):
        (self.output / "libSkiaSharp.dll").write_bytes(native_pe() + b"tampering")
        with self.assertRaisesRegex(ValueError, "no matching"):
            self.validate()

    def test_extracted_package_tampering_rejected(self):
        self.native.write_bytes(native_pe() + b"tampering")
        with self.assertRaisesRegex(ValueError, "differs from NuGet archive"):
            self.validate()

    def test_archive_tampering_rejected(self):
        with (self.package / "skia.1.0.nupkg").open("ab") as stream:
            stream.write(b"tampering")
        with self.assertRaisesRegex(ValueError, "archive hash differs"):
            self.validate()

    def test_legacy_native_rejected(self):
        (self.output / "sqlite3.dll").write_bytes(native_pe())
        with self.assertRaisesRegex(ValueError, "legacy component"):
            self.validate()

    def test_missing_skia_rejected(self):
        (self.output / "libSkiaSharp.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Skia native asset is missing"):
            self.validate()

    def test_non_windows_native_rejected(self):
        for name in ("libSkiaSharp.dylib", "libSkiaSharp.so", "libSkiaSharp.so.1"):
            with self.subTest(name=name):
                path = self.output / name
                path.write_bytes(b"non-Windows native asset")
                with self.assertRaisesRegex(ValueError, "Non-Windows"):
                    self.validate()
                path.unlink()


class AuditGateTests(unittest.TestCase):
    clean = {"version": 1, "projects": [{"path": "app.csproj", "frameworks": [{"framework": "net10.0"}]}]}

    def test_clean_report_passes(self):
        vulnerabilities.check(self.clean)

    def test_feed_problem_fails(self):
        report = dict(self.clean, problems=[{"text": "feed unavailable"}])
        with self.assertRaises(ValueError):
            vulnerabilities.check(report)

    def test_empty_result_fails(self):
        with self.assertRaises(ValueError):
            vulnerabilities.check({"version": 1, "projects": []})

    def test_each_severity_and_dependency_kind_fails(self):
        for severity in ("Low", "Moderate", "High", "Critical"):
            for kind in ("topLevelPackages", "transitivePackages"):
                with self.subTest(severity=severity, kind=kind):
                    report = copy.deepcopy(self.clean)
                    report["projects"][0]["frameworks"][0][kind] = [{
                        "id": "bad", "resolvedVersion": "1.0", "vulnerabilities": [{"severity": severity}]}]
                    with self.assertRaises(ValueError):
                        vulnerabilities.check(report)


@unittest.skipUnless(os.environ.get("MCD_PACKAGE_INTEGRITY_TOOL"), "SDK verifier integration enabled in package CI")
class SignedPackageIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tool = os.environ["MCD_PACKAGE_INTEGRITY_TOOL"]
        assets = json.loads(Path("MCDSaveEdit/obj/project.assets.json").read_text())
        library = next(value for key, value in assets["libraries"].items()
                       if key.startswith("SkiaSharp.NativeAssets.Win32/"))
        cls.expected = library["sha512"]
        root = next(Path(root) / library["path"] for root in assets["packageFolders"]
                    if (Path(root) / library["path"]).is_dir())
        cls.archive = next(root.glob("*.nupkg"))

    def verify(self, archive, expected=None):
        return subprocess.run([os.environ.get("DOTNET_HOST_PATH", "dotnet"), self.tool,
                               str(archive), expected or self.expected],
                              capture_output=True, text=True, check=False)

    def test_real_signed_archive_canonical_hash_passes(self):
        result = self.verify(self.archive)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["contentHash"], self.expected)
        self.assertTrue(json.loads(result.stdout)["signed"])

    def test_wrong_locked_hash_fails(self):
        self.assertNotEqual(self.verify(self.archive, "invalid-lock-hash").returncode, 0)

    def test_signed_archive_payload_tampering_fails(self):
        with tempfile.TemporaryDirectory() as temp:
            tampered = Path(temp) / "tampered.nupkg"
            with zipfile.ZipFile(self.archive) as original, zipfile.ZipFile(tampered, "w") as modified:
                for entry in original.infolist():
                    data = original.read(entry.filename)
                    if entry.filename == "runtimes/win-x64/native/libSkiaSharp.dll":
                        data += b"tampering"
                    modified.writestr(entry, data)
            result = self.verify(tampered)
            self.assertNotEqual(result.returncode, 0)


if __name__ == "__main__":
    unittest.main()
