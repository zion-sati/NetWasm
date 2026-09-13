#!/usr/bin/env python3

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


_STAGER_PATH = Path(__file__).parent.parent / "stage-wasm-tools.py"
_SPEC = importlib.util.spec_from_file_location("stage_wasm_tools", _STAGER_PATH)
assert _SPEC is not None and _SPEC.loader is not None
stager = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = stager
_SPEC.loader.exec_module(stager)


class StageWasmToolsTests(unittest.TestCase):
    def test_stage_accepts_only_the_pinned_cached_distribution(self):
        files = {
            "LICENSE": b"license",
            "README.md": b"readme",
            "wasm-tools.wasm": b"module",
        }
        archive = self._archive("fixture", files)
        archive_hash = hashlib.sha256(archive).hexdigest()
        expected = {
            name: hashlib.sha256(content).hexdigest()
            for name, content in files.items()
        }

        with tempfile.TemporaryDirectory() as directory, self._pins(
            "fixture", archive_hash, expected
        ):
            root = Path(directory)
            cache = root / "cache"
            cache.mkdir()
            (cache / f"sha256-{archive_hash}.tar.gz").write_bytes(archive)
            generation = root / "generation"

            self.assertEqual(len(files), stager.stage(generation, cache))
            self.assertEqual(files, {
                path.name: path.read_bytes()
                for path in (generation / "payload").iterdir()
            })
            marker = json.loads((generation / "generation.json").read_text())
            self.assertEqual("complete", marker["state"])
            self.assertEqual(len(files), marker["fileCount"])

            with self.assertRaises(stager.WasmToolsStagingError):
                stager.stage(generation, cache)

    def test_cache_rejects_tampered_bytes(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.object(
            stager, "ARCHIVE_SHA256", "0" * 64
        ):
            cache = Path(directory)
            (cache / f"sha256-{'0' * 64}.tar.gz").write_bytes(b"tampered")
            with self.assertRaises(stager.WasmToolsStagingError):
                stager._download_archive(cache)

    def test_download_requires_https_and_the_pinned_hash(self):
        content = b"archive"
        archive_hash = hashlib.sha256(content).hexdigest()

        class Response:
            def __init__(self, url: str):
                self._url = url
                self._read = False

            def __enter__(self):
                return self

            def __exit__(self, *_):
                return False

            def geturl(self):
                return self._url

            def read(self, _):
                if self._read:
                    return b""
                self._read = True
                return content

        with tempfile.TemporaryDirectory() as directory, mock.patch.object(
            stager, "ARCHIVE_SHA256", archive_hash
        ), mock.patch.object(
            stager.urllib.request,
            "urlopen",
            return_value=Response("https://objects.githubusercontent.com/release"),
        ):
            archive = stager._download_archive(Path(directory))
            self.assertEqual(content, archive.read_bytes())

        with tempfile.TemporaryDirectory() as directory, mock.patch.object(
            stager, "ARCHIVE_SHA256", archive_hash
        ), mock.patch.object(
            stager.urllib.request,
            "urlopen",
            return_value=Response("http://example.test/release"),
        ):
            with self.assertRaises(stager.WasmToolsStagingError):
                stager._download_archive(Path(directory))

    def test_distribution_rejects_extra_and_link_entries(self):
        with tempfile.TemporaryDirectory() as directory:
            extra = Path(directory) / "extra.tar.gz"
            extra.write_bytes(self._archive("fixture", {"extra": b"extra"}))
            with mock.patch.object(stager, "ARCHIVE_DIRECTORY", "fixture"), \
                 mock.patch.dict(stager.EXPECTED_FILES, {}, clear=True):
                with self.assertRaises(stager.WasmToolsStagingError):
                    stager._read_distribution(extra)

            linked = Path(directory) / "linked.tar.gz"
            payload = io.BytesIO()
            with tarfile.open(fileobj=payload, mode="w:gz") as package:
                root = tarfile.TarInfo("fixture")
                root.type = tarfile.DIRTYPE
                package.addfile(root)
                link = tarfile.TarInfo("fixture/module")
                link.type = tarfile.SYMTYPE
                link.linkname = "outside"
                package.addfile(link)
            linked.write_bytes(payload.getvalue())
            with mock.patch.object(stager, "ARCHIVE_DIRECTORY", "fixture"), \
                 mock.patch.dict(stager.EXPECTED_FILES, {"module": "0" * 64}, clear=True):
                with self.assertRaises(stager.WasmToolsStagingError):
                    stager._read_distribution(linked)

    def test_generation_and_cache_must_not_overlap(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with self.assertRaises(stager.WasmToolsStagingError):
                stager.stage(root / "generation", root)

    @staticmethod
    def _archive(root_name: str, files: dict[str, bytes]) -> bytes:
        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w:gz") as package:
            root = tarfile.TarInfo(root_name)
            root.type = tarfile.DIRTYPE
            package.addfile(root)
            for name, content in files.items():
                entry = tarfile.TarInfo(f"{root_name}/{name}")
                entry.size = len(content)
                package.addfile(entry, io.BytesIO(content))
        return payload.getvalue()

    @staticmethod
    def _pins(root: str, archive_hash: str, files: dict[str, str]):
        return _PinContext(root, archive_hash, files)


class _PinContext:
    def __init__(self, root: str, archive_hash: str, files: dict[str, str]):
        self._patches = [
            mock.patch.object(stager, "ARCHIVE_DIRECTORY", root),
            mock.patch.object(stager, "ARCHIVE_NAME", f"{root}.tar.gz"),
            mock.patch.object(stager, "ARCHIVE_SHA256", archive_hash),
            mock.patch.dict(stager.EXPECTED_FILES, files, clear=True),
        ]

    def __enter__(self):
        for patch in self._patches:
            patch.start()
        return self

    def __exit__(self, *exception):
        for patch in reversed(self._patches):
            patch.stop()
        return False


if __name__ == "__main__":
    unittest.main()
