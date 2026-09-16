#!/usr/bin/env python3

from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).parents[1] / "project-release-version.py"
SPEC = importlib.util.spec_from_file_location("project_release_version", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ProjectReleaseVersionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        subprocess.run(["git", "init", "--quiet", str(self.root)], check=True)
        subprocess.run(["git", "-C", str(self.root), "config", "user.name", "Test"], check=True)
        subprocess.run(
            ["git", "-C", str(self.root), "config", "user.email", "test@example.invalid"],
            check=True,
        )
        (self.root / "eng").mkdir()
        (self.root / "src/Example").mkdir(parents=True)
        (self.root / "eng/NetWasm.ReleaseVersion.txt").write_text("0.1.0-rc.1\n")
        (self.root / "src/Example/Example.csproj").write_text(
            "Package=0.1.0-rc.1\nDependency=0.1.0-rc.1\n"
        )
        (self.root / "binary.dat").write_bytes(b"\0" + b"0.1.0-rc.1")
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True)
        subprocess.run(["git", "-C", str(self.root), "commit", "--quiet", "-m", "fixture"], check=True)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_projects_all_tracked_text_and_records_receipt(self) -> None:
        receipt_path = self.root.parent / f"{self.root.name}-receipt.json"
        receipt = MODULE.project_version(self.root, "0.1.0-alpha.1", receipt_path)

        self.assertEqual("0.1.0-alpha.1\n", (self.root / "eng/NetWasm.ReleaseVersion.txt").read_text())
        self.assertNotIn(
            "0.1.0-rc.1", (self.root / "src/Example/Example.csproj").read_text()
        )
        self.assertEqual(b"\0" + b"0.1.0-rc.1", (self.root / "binary.dat").read_bytes())
        self.assertEqual(3, receipt["replacementCount"])
        self.assertEqual(receipt, json.loads(receipt_path.read_text()))
        receipt_path.unlink()

    def test_rejects_invalid_version(self) -> None:
        with self.assertRaisesRegex(ValueError, "Invalid release version"):
            MODULE.project_version(self.root, "not a version", self.root / "receipt.json")

    def test_stable_projection_preserves_pinned_measurement_evidence(self) -> None:
        measurement = self.root / "docs/size-and-methodology.md"
        measurement.parent.mkdir()
        measurement.write_text("Measured with 0.1.0-rc.1; reproduce using 0.1.0-rc.1.\n")
        subprocess.run(["git", "-C", str(self.root), "add", "docs"], check=True)
        subprocess.run(["git", "-C", str(self.root), "commit", "--quiet", "-m", "measurement"], check=True)
        original = measurement.read_bytes()

        receipt = MODULE.project_version(self.root, "0.1.0", self.root / "receipt.json")

        self.assertEqual(original, measurement.read_bytes())
        self.assertEqual("0.1.0\n", (self.root / "eng/NetWasm.ReleaseVersion.txt").read_text())
        self.assertEqual(
            "Package=0.1.0\nDependency=0.1.0\n",
            (self.root / "src/Example/Example.csproj").read_text(),
        )
        self.assertEqual(3, receipt["replacementCount"])
        self.assertNotIn("docs/size-and-methodology.md", [item["path"] for item in receipt["changedFiles"]])

    def test_stable_projection_preserves_larger_numeric_tokens(self) -> None:
        (self.root / "eng/NetWasm.ReleaseVersion.txt").write_text("1.2.3\n")
        (self.root / "src/Example/Example.csproj").write_text(
            "Package=1.2.3\nPosixRule=M11.2.3\nAssemblyVersion=1.2.3.4\n"
        )
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True)
        subprocess.run(
            ["git", "-C", str(self.root), "commit", "--quiet", "-m", "stable fixture"],
            check=True,
        )

        receipt = MODULE.project_version(self.root, "2.3.4", self.root / "receipt.json")

        self.assertEqual("2.3.4\n", (self.root / "eng/NetWasm.ReleaseVersion.txt").read_text())
        self.assertEqual(
            "Package=2.3.4\nPosixRule=M11.2.3\nAssemblyVersion=1.2.3.4\n",
            (self.root / "src/Example/Example.csproj").read_text(),
        )
        self.assertEqual(2, receipt["replacementCount"])

    def test_prerelease_projection_does_not_rewrite_protocol_versions(self) -> None:
        (self.root / "eng/NetWasm.ReleaseVersion.txt").write_text("0.2.0\n")
        (self.root / "src/Example/Example.csproj").write_text("Package=0.2.0\n")
        protocol = self.root / "wit/protocol.wit"
        protocol.parent.mkdir()
        protocol.write_text("package wasi:io@0.2.0;\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(self.root), "add", "."], check=True)
        subprocess.run(
            ["git", "-C", str(self.root), "commit", "--quiet", "-m", "protocol fixture"],
            check=True,
        )

        receipt = MODULE.project_version(
            self.root, "0.2.0-preview.1", self.root / "receipt.json"
        )

        self.assertEqual("Package=0.2.0-preview.1\n", (self.root / "src/Example/Example.csproj").read_text())
        self.assertEqual("package wasi:io@0.2.0;\n", protocol.read_text(encoding="utf-8"))
        self.assertEqual(2, receipt["replacementCount"])

    def test_source_version_is_a_no_op(self) -> None:
        receipt_path = self.root.parent / f"{self.root.name}-receipt.json"
        receipt = MODULE.project_version(self.root, "0.1.0-rc.1", receipt_path)

        self.assertEqual([], receipt["changedFiles"])
        self.assertEqual(0, receipt["replacementCount"])
        self.assertEqual(b"", subprocess.check_output(["git", "-C", str(self.root), "status", "--porcelain=v1"]))
        receipt_path.unlink()


if __name__ == "__main__":
    unittest.main()
