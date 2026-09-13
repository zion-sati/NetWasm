#!/usr/bin/env python3

import base64
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest
from unittest import mock


_STAGER_PATH = Path(__file__).parent.parent / "stage-binaryen.py"
_SPEC = importlib.util.spec_from_file_location("stage_binaryen", _STAGER_PATH)
assert _SPEC is not None and _SPEC.loader is not None
stager = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = stager
_SPEC.loader.exec_module(stager)


class StageBinaryenTests(unittest.TestCase):
    def test_checked_in_lock_and_policy_select_binaryen_132(self):
        eng = Path(__file__).parent.parent
        record, policy = stager._read_inputs(
            eng / "binaryen-package-lock.json",
            eng / "binaryen-closure.json",
        )
        self.assertEqual("132.0.0", record["version"])
        self.assertEqual("binaryen", policy["package"]["name"])

    def test_stage_extracts_one_locked_package(self):
        archive = self._archive()
        integrity = "sha512-" + base64.b64encode(
            hashlib.sha512(archive).digest()
        ).decode("ascii")
        record = {
            "version": "1.0.0",
            "resolved": "https://example.test/binaryen.tgz",
            "integrity": integrity,
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            lock = {
                "name": "fixture",
                "lockfileVersion": 3,
                "packages": {
                    "": {
                        "name": "fixture",
                        "version": "1.0.0",
                        "devDependencies": {"binaryen": "1.0.0"},
                    },
                    stager.PACKAGE_PATH: record,
                },
            }
            lock_path = root / "package-lock.json"
            lock_path.write_text(json.dumps(lock), encoding="utf-8")
            policy = {
                "schemaVersion": "1",
                "contract": "binaryen-programs",
                "lockSha256": hashlib.sha256(lock_path.read_bytes()).hexdigest(),
                "package": {
                    "path": stager.PACKAGE_PATH,
                    "name": "binaryen",
                    "version": record["version"],
                    "resolved": record["resolved"],
                    "integrity": record["integrity"],
                },
            }
            policy_path = root / "policy.json"
            policy_path.write_text(json.dumps(policy), encoding="utf-8")
            generation = root / "generation"

            with mock.patch.object(
                stager._SUPPORT, "_cache_tarball", return_value=archive
            ):
                self.assertEqual(
                    2,
                    stager.stage(lock_path, policy_path, generation, root / "cache"),
                )
            self.assertTrue((generation / "payload/index.js").is_file())
            marker = json.loads((generation / "generation.json").read_text())
            self.assertEqual("complete", marker["state"])

    def test_policy_hash_and_lock_inventory_are_fail_closed(self):
        eng = Path(__file__).parent.parent
        policy = json.loads((eng / "binaryen-closure.json").read_text())
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            policy["lockSha256"] = "0" * 64
            policy_path = root / "policy.json"
            policy_path.write_text(json.dumps(policy), encoding="utf-8")
            with self.assertRaises(stager.BinaryenStagingError):
                stager._read_inputs(eng / "binaryen-package-lock.json", policy_path)

            lock = json.loads((eng / "binaryen-package-lock.json").read_text())
            lock["packages"]["node_modules/unexpected"] = lock["packages"][
                stager.PACKAGE_PATH
            ]
            lock_path = root / "package-lock.json"
            lock_path.write_text(json.dumps(lock), encoding="utf-8")
            policy["lockSha256"] = hashlib.sha256(lock_path.read_bytes()).hexdigest()
            policy_path.write_text(json.dumps(policy), encoding="utf-8")
            with self.assertRaises(stager.BinaryenStagingError):
                stager._read_inputs(lock_path, policy_path)

    @staticmethod
    def _archive() -> bytes:
        payload = io.BytesIO()
        files = {
            "package/package.json": json.dumps(
                {"name": "binaryen", "version": "1.0.0"}
            ).encode("utf-8"),
            "package/index.js": b"export {};",
        }
        with tarfile.open(fileobj=payload, mode="w:gz") as package:
            root = tarfile.TarInfo("package")
            root.type = tarfile.DIRTYPE
            package.addfile(root)
            for name, content in files.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                package.addfile(entry, io.BytesIO(content))
        return payload.getvalue()


if __name__ == "__main__":
    unittest.main()
