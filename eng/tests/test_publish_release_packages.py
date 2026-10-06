from __future__ import annotations

import importlib.util
import io
import json
import stat
import subprocess
import tempfile
import unittest
import urllib.error
import warnings
import zipfile
from pathlib import Path
from unittest import mock


SCRIPT = Path(__file__).parents[1] / "publish-release-packages.py"
SPEC = importlib.util.spec_from_file_location("publish_release_packages", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def archive_bytes(entries: list[tuple[str, bytes, int]]) -> bytes:
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w") as archive:
        for name, content, permissions in entries:
            info = zipfile.ZipInfo(name)
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | permissions) << 16
            archive.writestr(info, content)
    return output.getvalue()


class PublishReleasePackagesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.manifest = {
            "releaseVersion": "0.3.0-preview.1",
            "packages": [
                "NetWasm.Sdk",
                "NetWasm.Toolchain",
                "NetWasm.HostTools.linux-x64",
                "NetWasm.Templates",
            ],
        }
        for package_id in self.manifest["packages"]:
            self.create_package(package_id)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def create_package(
        self, package_id: str, dependencies: tuple[str, ...] = ()
    ) -> Path:
        path = self.root / f"{package_id}.0.3.0-preview.1.nupkg"
        rows = "".join(
            f'<dependency id="{dependency}" version="[0.3.0-preview.1]" />'
            for dependency in dependencies
        )
        nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>0.3.0-preview.1</version>
    <authors>Zion Sati</authors>
    <description>Test package.</description>
    <dependencies><group targetFramework="net10.0">{rows}</group></dependencies>
  </metadata>
</package>
"""
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(f"{package_id}.nuspec", nuspec)
        return path

    def test_publishes_prerequisites_then_sdk_and_templates_together(self) -> None:
        stages = MODULE.publication_stages(self.manifest, self.root)

        self.assertEqual(
            [
                ["NetWasm.HostTools.linux-x64", "NetWasm.Toolchain"],
                ["NetWasm.Sdk", "NetWasm.Templates"],
            ],
            [[package_id for package_id, _ in stage] for stage in stages],
        )

    def test_rejects_missing_and_extra_files_before_publishing(self) -> None:
        (self.root / "NetWasm.Toolchain.0.3.0-preview.1.nupkg").unlink()
        with self.assertRaisesRegex(ValueError, "do not match"):
            MODULE.publication_stages(self.manifest, self.root)

        self.create_package("NetWasm.Toolchain")
        self.create_package("NetWasm.Unexpected")
        with self.assertRaisesRegex(ValueError, "do not match"):
            MODULE.publication_stages(self.manifest, self.root)

    def test_rejects_manifest_without_dependent_packages(self) -> None:
        manifest = dict(self.manifest, packages=["NetWasm.Toolchain"])
        with self.assertRaisesRegex(ValueError, "SDK and templates"):
            MODULE.publication_stages(manifest, self.root)

    def test_rejects_templates_sdk_dependency_before_publishing(self) -> None:
        self.create_package("NetWasm.Templates", ("NetWasm.Sdk",))

        with self.assertRaisesRegex(ValueError, "cannot share"):
            MODULE.publication_stages(self.manifest, self.root)

    def test_discovers_package_content_endpoint(self) -> None:
        payload = json.dumps({"resources": [
            {"@type": "Other/1.0.0", "@id": "https://ignored.example/"},
            {"@type": ["PackageBaseAddress/3.0.0"],
             "@id": "https://example.invalid/packages/"},
        ]}).encode()
        fetch = mock.Mock(return_value=io.BytesIO(payload))

        self.assertEqual(
            "https://example.invalid/packages/", MODULE.package_base_address(fetch)
        )
        self.assertEqual(MODULE.SOURCE_INDEX, fetch.call_args.args[0])

    def test_feed_probe_uses_head_and_treats_not_found_as_pending(self) -> None:
        response = mock.MagicMock()
        response.status = 200
        response.__enter__.return_value = response
        index = io.BytesIO(b'{"versions":["0.3.0-preview.1"]}')
        fetch = mock.Mock(side_effect=(index, response))

        self.assertTrue(MODULE.available_on_feed(
            "NetWasm.Toolchain", "0.3.0-preview.1",
            "https://example.invalid/packages/", fetch,
        ))
        self.assertEqual(
            "https://example.invalid/packages/netwasm.toolchain/index.json",
            fetch.call_args_list[0].args[0],
        )
        request = fetch.call_args_list[1].args[0]
        self.assertEqual("HEAD", request.get_method())
        self.assertEqual(
            "https://example.invalid/packages/netwasm.toolchain/"
            "0.3.0-preview.1/netwasm.toolchain.0.3.0-preview.1.nupkg",
            request.full_url,
        )

        missing = urllib.error.HTTPError(request.full_url, 404, "missing", None, None)
        try:
            self.assertFalse(MODULE.available_on_feed(
                "NetWasm.Toolchain", "0.3.0-preview.1",
                "https://example.invalid/packages/", mock.Mock(side_effect=missing),
            ))
        finally:
            missing.close()

    def test_feed_probe_waits_for_version_index(self) -> None:
        fetch = mock.Mock(return_value=io.BytesIO(b'{"versions":["0.2.4"]}'))

        self.assertFalse(MODULE.available_on_feed(
            "NetWasm.Toolchain", "0.3.0-preview.1",
            "https://example.invalid/packages/", fetch,
        ))
        self.assertEqual(1, fetch.call_count)

    def test_feed_probe_retries_throttling_and_server_errors(self) -> None:
        for status in (429, 500, 503):
            with self.subTest(status=status):
                error = urllib.error.HTTPError(
                    "https://example.invalid/packages/", status, "pending", None, None
                )
                try:
                    self.assertFalse(MODULE.available_on_feed(
                        "NetWasm.Toolchain", "0.3.0-preview.1",
                        "https://example.invalid/packages/",
                        mock.Mock(side_effect=error),
                    ))
                finally:
                    error.close()


    def test_accepts_only_repository_signature_difference(self) -> None:
        local = [
            ("NetWasm.Toolchain.nuspec", b"source-commit=a", 0o644),
            ("tools/bin/node", b"pinned executable", 0o755),
        ]
        local_path = self.root / "payload.nupkg"
        local_path.write_bytes(archive_bytes(local))
        remote = archive_bytes(local + [(".signature.p7s", b"repository signature", 0o644)])

        MODULE.verify_feed_payload(
            "NetWasm.Toolchain", "0.3.0-preview.1", local_path,
            "https://example.invalid/packages/",
            fetch=mock.Mock(return_value=io.BytesIO(remote)),
        )

        for changed in (
            [("NetWasm.Toolchain.nuspec", b"source-commit=b", 0o644),
             ("tools/bin/node", b"pinned executable", 0o755)],
            [("NetWasm.Toolchain.nuspec", b"source-commit=a", 0o644),
             ("tools/bin/node", b"pinned executable", 0o644)],
            local + [("tools/extra", b"unexpected", 0o644)],
        ):
            with self.subTest(changed=changed), self.assertRaisesRegex(
                ValueError, "content differs"
            ):
                MODULE.verify_feed_payload(
                    "NetWasm.Toolchain", "0.3.0-preview.1", local_path,
                    "https://example.invalid/packages/",
                    fetch=mock.Mock(return_value=io.BytesIO(archive_bytes(changed))),
                )

    def test_rejects_unsafe_or_duplicate_package_entries(self) -> None:
        for name in ("../escape", "/absolute", "a/./b", "a//b", "C:/escape"):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "unsafe"):
                with zipfile.ZipFile(io.BytesIO(archive_bytes([(name, b"x", 0o644)]))) as archive:
                    MODULE.payload_digests(archive)
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", UserWarning)
            repeated = archive_bytes([
                ("same", b"one", 0o644), ("same", b"two", 0o644),
            ])
        with zipfile.ZipFile(io.BytesIO(repeated)) as archive:
            with self.assertRaisesRegex(ValueError, "duplicate"):
                MODULE.payload_digests(archive)

    def test_partial_release_resumes_in_two_dependency_ordered_stages(self) -> None:
        events = []
        available = {"NetWasm.HostTools.linux-x64"}

        def probe(package_id: str, version: str, base: str) -> bool:
            events.append(("probe", package_id))
            return package_id in available

        def push(command: list[str], *, check: bool, timeout: float) -> object:
            self.assertFalse(check)
            self.assertEqual("dotnet", command[0])
            self.assertNotIn("--api-key", command)
            self.assertEqual(MODULE.PUSH_TIMEOUT_SECONDS, timeout)
            package_id = Path(command[3]).name.removesuffix(".0.3.0-preview.1.nupkg")
            available.add(package_id)
            events.append(("push", package_id))
            return mock.Mock(returncode=0)

        with mock.patch.object(MODULE, "package_base_address", return_value="https://example.invalid/"), \
             mock.patch.object(MODULE, "available_on_feed", side_effect=probe), \
             mock.patch.object(MODULE, "verify_feed_payload", side_effect=lambda package_id, *args: events.append(("verify", package_id))), \
             mock.patch.object(MODULE.subprocess, "run", side_effect=push):
            with mock.patch.dict(MODULE.os.environ, {"NUGET_API_KEY": "secret"}):
                for stage in MODULE.STAGE_NAMES:
                    MODULE.publish_stage(self.manifest, self.root, stage)
                    events.append(("complete", stage))

        self.assertEqual(
            {"NetWasm.Toolchain", "NetWasm.Sdk", "NetWasm.Templates"},
            {value for operation, value in events if operation == "push"},
        )
        prerequisite_complete = events.index(("complete", "prerequisites"))
        self.assertLess(prerequisite_complete, events.index(("push", "NetWasm.Sdk")))
        self.assertLess(prerequisite_complete, events.index(("push", "NetWasm.Templates")))
        self.assertEqual(
            [("verify", "NetWasm.HostTools.linux-x64")],
            [event for event in events if event[0] == "verify"],
        )
    def test_prerequisites_publish_sequentially_in_dependency_safe_order(self) -> None:
        for index in range(8):
            package_id = f"NetWasm.Independent{index}"
            self.manifest["packages"].append(package_id)
            self.create_package(package_id)
        published: list[str] = []

        def push(package_id: str, *args) -> bool:
            published.append(package_id)
            return False

        with mock.patch.object(MODULE, "package_base_address", return_value="https://example.invalid/"), \
             mock.patch.object(MODULE, "push_or_reconcile", side_effect=push):
            MODULE.publish_stage(self.manifest, self.root, "prerequisites")

        expected = [
            package_id
            for package_id, _ in MODULE.publication_stages(self.manifest, self.root)[0]
        ]
        self.assertEqual(expected, published)

    def test_preflight_reconciles_existing_payloads_without_a_credential(self) -> None:
        with mock.patch.object(MODULE, "package_base_address", return_value="https://example.invalid/"), \
             mock.patch.object(MODULE, "available_on_feed", side_effect=lambda package_id, *args: package_id == "NetWasm.Sdk"), \
             mock.patch.object(MODULE, "verify_feed_payload") as verify:
            result = MODULE.preflight_release(self.manifest, self.root)

        self.assertEqual(["NetWasm.Sdk"], result["existing"])
        self.assertEqual(3, len(result["absent"]))
        verify.assert_called_once()

    def test_completed_release_retry_skips_every_push(self) -> None:
        verified = []
        with mock.patch.object(MODULE, "package_base_address", return_value="https://example.invalid/"), \
             mock.patch.object(MODULE, "available_on_feed", return_value=True), \
             mock.patch.object(MODULE, "verify_feed_payload", side_effect=lambda package_id, *args: verified.append(package_id)), \
             mock.patch.object(MODULE.subprocess, "run") as push:
            for stage in MODULE.STAGE_NAMES:
                MODULE.publish_stage(self.manifest, self.root, stage)

        self.assertEqual(set(self.manifest["packages"]), set(verified))
        push.assert_not_called()

    def test_existing_mismatched_package_blocks_publish(self) -> None:
        with mock.patch.object(MODULE, "available_on_feed", return_value=True), \
             mock.patch.object(MODULE, "verify_feed_payload", side_effect=ValueError("content differs")), \
             mock.patch.object(MODULE.subprocess, "run") as push:
            with self.assertRaisesRegex(ValueError, "content differs"):
                MODULE.push_or_reconcile(
                    "NetWasm.Toolchain", "0.3.0-preview.1",
                    self.root / "NetWasm.Toolchain.0.3.0-preview.1.nupkg",
                    "https://example.invalid/",
                )
        push.assert_not_called()

    def test_timed_out_push_fails_without_polling_the_feed(self) -> None:
        probe = mock.Mock(return_value=False)
        with mock.patch.object(MODULE, "available_on_feed", probe), \
             mock.patch.object(MODULE, "verify_feed_payload") as verify, \
             mock.patch.object(MODULE.subprocess, "run", side_effect=subprocess.TimeoutExpired("dotnet", 10)), \
             mock.patch.dict(MODULE.os.environ, {"NUGET_API_KEY": "secret"}):
            with self.assertRaisesRegex(TimeoutError, "Retry the release"):
                MODULE.push_or_reconcile(
                    "NetWasm.Toolchain", "0.3.0-preview.1",
                    self.root / "NetWasm.Toolchain.0.3.0-preview.1.nupkg",
                    "https://example.invalid/",
                )
        probe.assert_called_once()
        verify.assert_not_called()

    def test_failed_push_is_not_retried_or_reconciled(self) -> None:
        run = mock.Mock(return_value=mock.Mock(returncode=1))
        probe = mock.Mock(return_value=False)
        with mock.patch.object(MODULE, "available_on_feed", probe), \
             mock.patch.object(MODULE, "verify_feed_payload") as verify, \
             mock.patch.object(MODULE.subprocess, "run", run), \
             mock.patch.dict(MODULE.os.environ, {"NUGET_API_KEY": "secret"}):
            with self.assertRaisesRegex(RuntimeError, "Publishing failed"):
                MODULE.push_or_reconcile(
                    "NetWasm.Toolchain", "0.3.0-preview.1",
                    self.root / "NetWasm.Toolchain.0.3.0-preview.1.nupkg",
                    "https://example.invalid/",
                )
        self.assertEqual(1, run.call_count)
        probe.assert_called_once()
        verify.assert_not_called()

if __name__ == "__main__":
    unittest.main()
