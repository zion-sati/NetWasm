from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "verify-release-packages.py"
SPEC = importlib.util.spec_from_file_location("verify_release_packages", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class VerifyReleasePackagesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.manifest = {
            "schemaVersion": 1,
            "repository": "zion-sati/Example",
            "repositoryUrl": "https://github.com/zion-sati/Example",
            "releaseVersion": "0.1.0-rc.1",
            "releaseTag": "v0.1.0-rc.1",
            "sourceCommit": "a" * 40,
            "packages": ["NetWasm.Example"],
        }

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def create_package(
        self,
        *,
        package_id: str = "NetWasm.Example",
        version: str = "0.1.0-rc.1",
        dependency_version: str = "[0.1.0-rc.1]",
        repository_commit: str | None = None,
    ) -> Path:
        repository_commit = repository_commit or "a" * 40
        path = self.root / f"{package_id}.{version}.nupkg"
        nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>{version}</version>
    <authors>Zion Sati</authors>
    <description>Test package.</description>
    <repository type="git" url="https://github.com/zion-sati/Example" commit="{repository_commit}" />
    <dependencies>
      <group targetFramework="NetWasm,Version=v0.1">
        <dependency id="NetWasm.Dependency" version="{dependency_version}" />
      </group>
    </dependencies>
  </metadata>
</package>
"""
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(f"{package_id}.nuspec", nuspec)
        return path

    def test_accepts_exact_allowlisted_package(self) -> None:
        self.create_package()

        packages = MODULE.validate_packages(self.root, self.manifest)

        self.assertEqual(["NetWasm.Example"], [package["id"] for package in packages])
        self.assertEqual("a" * 40, packages[0]["repositoryCommit"])

    def test_rejects_unexpected_package(self) -> None:
        self.create_package(package_id="NetWasm.Unexpected")

        with self.assertRaisesRegex(ValueError, "allowlist mismatch"):
            MODULE.validate_packages(self.root, self.manifest)

    def test_rejects_unpinned_netwasm_dependency(self) -> None:
        self.create_package(dependency_version="0.1.0-rc.1")

        with self.assertRaisesRegex(ValueError, "not pinned"):
            MODULE.validate_packages(self.root, self.manifest)

    def test_rejects_wrong_repository_commit(self) -> None:
        self.create_package(repository_commit="b" * 40)

        with self.assertRaisesRegex(ValueError, "repository commit"):
            MODULE.validate_packages(self.root, self.manifest)

    def test_writes_hash_bound_receipt(self) -> None:
        self.create_package()
        packages = MODULE.validate_packages(self.root, self.manifest)
        receipt_path = self.root / "receipt.json"

        MODULE.write_receipt(receipt_path, self.manifest, packages)

        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        self.assertEqual("PASS", receipt["status"])
        self.assertEqual(1, receipt["packageCount"])
        self.assertEqual(64, len(receipt["packages"][0]["sha256"]))


if __name__ == "__main__":
    unittest.main()
