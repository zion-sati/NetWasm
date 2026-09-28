from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile


SCRIPT = Path(__file__).parents[1] / "release-train.py"
SPEC = importlib.util.spec_from_file_location("release_train", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ReleaseTrainTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.packages = self.root / "packages"
        self.packages.mkdir()
        self.commit = "a" * 40
        self.tag = "v0.5.0-preview.1"
        self.package = self.packages / "NetWasm.Example.0.5.0-preview.1.nupkg"
        self.package.write_bytes(b"package payload")
        self.manifest = self.root / "manifest.json"
        self.manifest.write_text(json.dumps({
            "schemaVersion": 1,
            "repository": "zion-sati/NetWasm",
            "repositoryUrl": "https://github.com/zion-sati/netwasm",
            "releaseVersion": "0.5.0-preview.1",
            "releaseTag": self.tag,
            "sourceCommit": self.commit,
            "packages": ["NetWasm.Example"],
        }))
        self.receipt = self.root / "receipt.json"
        self.receipt.write_text(json.dumps({
            "schemaVersion": 1,
            "status": "PASS",
            "repository": "zion-sati/NetWasm",
            "releaseVersion": "0.5.0-preview.1",
            "releaseTag": self.tag,
            "sourceCommit": self.commit,
            "packageCount": 1,
            "packages": [{
                "id": "NetWasm.Example",
                "version": "0.5.0-preview.1",
                "fileName": self.package.name,
                "size": self.package.stat().st_size,
                "sha256": hashlib.sha256(self.package.read_bytes()).hexdigest(),
            }],
        }))

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def create_bundle(self, name: str = "preview.zip") -> Path:
        output = self.root / name
        MODULE.create_bundle(self.manifest, self.receipt, self.packages, output)
        return output

    def test_bundle_round_trip_is_deterministic_and_receipt_bound(self) -> None:
        first = self.create_bundle("first.zip")
        second = self.create_bundle("second.zip")

        first_summary = MODULE.inspect_bundle(first)
        second_summary = MODULE.inspect_bundle(second)
        self.assertEqual(first.read_bytes(), second.read_bytes())
        self.assertEqual("0.5.0-preview.1", first_summary["version"])
        self.assertEqual(first_summary["packages"], second_summary["packages"])
        extracted = self.root / "extracted"
        MODULE.extract_bundle(first, extracted)
        self.assertEqual(
            b"package payload",
            (extracted / "packages" / self.package.name).read_bytes(),
        )

        self.package.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "does not match"):
            MODULE.create_bundle(
                self.manifest, self.receipt, self.packages, self.root / "changed.zip"
            )

    def test_train_binds_both_channels_to_source_run_and_target_tags(self) -> None:
        preview = self.create_bundle("preview.zip")
        stable_package = self.packages / "NetWasm.Example.0.5.0.nupkg"
        self.package.rename(stable_package)
        stable_manifest = json.loads(self.manifest.read_text())
        stable_manifest["releaseVersion"] = "0.5.0"
        stable_manifest_path = self.root / "stable-manifest.json"
        stable_manifest_path.write_text(json.dumps(stable_manifest))
        stable_receipt = json.loads(self.receipt.read_text())
        stable_receipt["releaseVersion"] = "0.5.0"
        stable_receipt["packages"][0].update({
            "version": "0.5.0",
            "fileName": stable_package.name,
        })
        stable_receipt_path = self.root / "stable-receipt.json"
        stable_receipt_path.write_text(json.dumps(stable_receipt))
        stable = self.root / "stable.zip"
        MODULE.create_bundle(stable_manifest_path, stable_receipt_path, self.packages, stable)
        toolchain = self.root / "toolchain.json"
        toolchain.write_text('{"emscripten":"6.0.7"}\n')

        train = MODULE.create_train_manifest(
            repository="zion-sati/NetWasm",
            source_commit=self.commit,
            producing_tag=self.tag,
            preview_tag=self.tag,
            stable_tag="v0.5.0",
            run_id="42",
            artifact_name="netwasm-release-train-0.5.0",
            preview_bundle=preview,
            stable_bundle=stable,
            toolchain=toolchain,
        )

        self.assertEqual("42", train["workflowRunId"])
        self.assertEqual("v0.5.0", train["stable"]["releaseTag"])
        self.assertEqual(
            ("42", "netwasm-release-train-0.5.0"),
            MODULE.verify_train_identity(
                train, "zion-sati/NetWasm", self.commit, self.tag,
                self.tag, "v0.5.0",
            ),
        )
        MODULE.verify_train(
            train, stable, "stable", "zion-sati/NetWasm", self.commit, "v0.5.0"
        )
        with self.assertRaisesRegex(ValueError, "does not target"):
            MODULE.verify_train(
                train, stable, "stable", "zion-sati/NetWasm", self.commit,
                "v0.5.1",
            )
        with self.assertRaisesRegex(ValueError, "sourceCommit"):
            MODULE.verify_train_identity(
                train, "zion-sati/NetWasm", "b" * 40, self.tag,
                self.tag, "v0.5.0",
            )

    def test_rejects_unsafe_and_unexpected_bundle_entries(self) -> None:
        path = self.root / "unsafe.zip"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("../escape", b"bad")
        with self.assertRaisesRegex(ValueError, "unsafe"):
            MODULE.inspect_bundle(path)


if __name__ == "__main__":
    unittest.main()
