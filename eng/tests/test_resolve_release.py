from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "resolve-release.py"
SPEC = importlib.util.spec_from_file_location("resolve_release", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ResolveReleaseTests(unittest.TestCase):
    def setUp(self) -> None:
        self.manifest = {
            "schemaVersion": 1,
            "repository": "zion-sati/Example",
            "repositoryUrl": "https://github.com/zion-sati/Example",
            "releaseVersion": "0.1.0",
            "releaseTag": "v0.1.0",
            "sourceCommit": "a" * 40,
            "packages": ["NetWasm.Example"],
        }

    def test_release_tag_is_version_authority(self) -> None:
        resolved = MODULE.resolve_manifest(
            self.manifest, "v0.2.0-preview.1", "b" * 40, "v"
        )

        self.assertEqual("0.2.0-preview.1", resolved["releaseVersion"])
        self.assertEqual("v0.2.0-preview.1", resolved["releaseTag"])
        self.assertEqual("b" * 40, resolved["sourceCommit"])
        self.assertEqual(["NetWasm.Example"], resolved["packages"])

    def test_supports_repository_specific_tag_prefix(self) -> None:
        resolved = MODULE.resolve_manifest(
            self.manifest, "netwasm-v0.2.0", "c" * 40, "netwasm-v"
        )

        self.assertEqual("0.2.0", resolved["releaseVersion"])

    def test_rejects_tag_without_expected_prefix(self) -> None:
        with self.assertRaisesRegex(ValueError, "must start"):
            MODULE.resolve_manifest(self.manifest, "0.2.0", "b" * 40, "v")

    def test_rejects_non_semantic_version(self) -> None:
        with self.assertRaisesRegex(ValueError, "semantic version"):
            MODULE.resolve_manifest(self.manifest, "vnext", "b" * 40, "v")

    def test_rejects_numeric_prerelease_with_leading_zero(self) -> None:
        with self.assertRaisesRegex(ValueError, "semantic version"):
            MODULE.resolve_manifest(self.manifest, "v0.2.0-preview.01", "b" * 40, "v")

    def test_rejects_short_source_commit(self) -> None:
        with self.assertRaisesRegex(ValueError, "full lowercase"):
            MODULE.resolve_manifest(self.manifest, "v0.2.0", "b" * 12, "v")


if __name__ == "__main__":
    unittest.main()
