#!/usr/bin/env python3

import base64
import hashlib
import importlib.util
import io
import json
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
from pathlib import Path


_STAGER_PATH = Path(__file__).parent.parent / "stage-jco-closure.py"
_SPEC = importlib.util.spec_from_file_location("stage_jco_closure", _STAGER_PATH)
assert _SPEC is not None and _SPEC.loader is not None
stager = importlib.util.module_from_spec(_SPEC)
sys.modules[_SPEC.name] = stager
_SPEC.loader.exec_module(stager)


class StageJcoClosureTests(unittest.TestCase):
    def test_policy_requires_explicit_transpile_contract_and_edges(self):
        root = stager.ROOT_PACKAGES[0]
        shim = stager.ROOT_PACKAGES[1]
        dependency = "node_modules/example"
        optional = "node_modules/optional"
        records = {
            root: self._record("@bytecodealliance/jco", "1"),
            shim: self._record("@bytecodealliance/preview2-shim", "1"),
            dependency: self._record("example", "1", optional_dependencies={"optional": "1"}),
            optional: self._record("optional", "1"),
        }
        records[root]["dependencies"] = {"example": "1"}
        lock = {"lockfileVersion": 3, "packages": records}
        policy = self._policy(
            lock,
            [root, shim, dependency],
            [
                {
                    "path": optional,
                    "reason": "not in the accepted runtime path",
                    "evidence": "fixture",
                }
            ],
        )

        selected = stager.select_locked_closure(lock, policy)
        self.assertEqual({root, shim, dependency}, set(selected))

        policy["excludedPackagePaths"] = []
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(lock, policy)

        lock["packages"][""]["devDependencies"].pop("@bytecodealliance/jco")
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(lock, self._policy(lock, [root, shim, dependency], [
                {
                    "path": optional,
                    "reason": "not in the accepted runtime path",
                    "evidence": "fixture",
                }
            ]))

    def test_policy_requires_and_traverses_non_optional_peer_edges(self):
        root = stager.ROOT_PACKAGES[0]
        shim = stager.ROOT_PACKAGES[1]
        peer = "node_modules/peer-runtime"
        records = {
            root: self._record("@bytecodealliance/jco", "1", peer_dependencies={"peer-runtime": "1"}),
            shim: self._record("@bytecodealliance/preview2-shim", "1"),
            peer: self._record("peer-runtime", "1"),
        }
        lock = {"lockfileVersion": 3, "packages": records}
        policy = self._policy(lock, [root, shim, peer], [])
        self.assertEqual({root, shim, peer}, set(stager.select_locked_closure(lock, policy)))

        excluded_policy = self._policy(
            lock,
            [root, shim],
            [{"path": peer, "reason": "fixture", "evidence": "fixture"}],
        )
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(lock, excluded_policy)

    def test_lock_dependency_values_and_package_targets_are_strict(self):
        root = stager.ROOT_PACKAGES[0]
        shim = stager.ROOT_PACKAGES[1]
        dependency = "node_modules/example"
        records = {
            root: self._record("@bytecodealliance/jco", "1"),
            shim: self._record("@bytecodealliance/preview2-shim", "1"),
            dependency: self._record("example", "1"),
        }
        records[root]["dependencies"] = {"example": 1}
        lock = {"lockfileVersion": 3, "packages": records}
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(lock, self._policy(lock, list(records), []))

        payload = self._package_tarball(
            {"package.json": '{"name":"fixture","version":"1"}', "index.js": "export {};"}
        )
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "package"
            target.mkdir()
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(payload, target, "1", "fixture")

    def test_archive_rejects_backslashes_duplicates_and_portability_aliases(self):
        for member in (
            "package\\package.json",
            "package/CON.txt",
            "package/NUL.txt",
            "package/COM1.txt",
            "package/name:stream",
            "package/name<invalid",
            "package/name>invalid",
            'package/name"invalid',
            "package/name|invalid",
            "package/name?invalid",
            "package/name*invalid",
            "package/name;invalid",
            "package/name$(invalid",
            "package/name@(invalid",
            "package/name%(invalid",
            "package/name.",
            "package/name\x01",
        ):
            with self.assertRaises(stager.ClosureStagingError):
                stager._safe_member_path(member)

        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w:gz") as archive:
            for name in ("package/package.json", "package/package.json"):
                info = tarfile.TarInfo(name)
                data = b'{"name":"fixture","version":"1"}'
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(payload.getvalue(), Path(directory) / "package", "1")

        alias_payload = self._package_tarball(
            {"package.json": '{"name":"fixture","version":"1"}', "\N{LATIN SMALL LETTER E WITH ACUTE}": "one", "e\N{COMBINING ACUTE ACCENT}": "two"}
        )
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(alias_payload, Path(directory) / "package", "1")

        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w:gz") as archive:
            info = tarfile.TarInfo("package")
            data = b"not a directory"
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(payload.getvalue(), Path(directory) / "package", "1")

    def test_extracted_package_declarations_must_match_the_lock_record(self):
        payload = self._package_tarball(
            {
                "package.json": json.dumps(
                    {
                        "name": "fixture",
                        "version": "1",
                        "dependencies": {"unexpected": "^1"},
                    }
                )
            }
        )
        record = {
            "version": "1",
            "dependencies": {},
            "optionalDependencies": {},
            "peerDependencies": {},
            "peerDependenciesMeta": {},
        }
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(
                    payload,
                    Path(directory) / "package",
                    "1",
                    "fixture",
                    record,
                )

    def test_sri_is_strict_and_cache_paths_are_digest_derived(self):
        payload = b"fixture tarball"
        digest = hashlib.sha512(payload).digest()
        integrity = "sha512-" + base64.b64encode(digest).decode("ascii")
        for invalid in (
            integrity + " " + integrity,
            integrity.replace("sha512-", "sha1-", 1),
            integrity[:-1],
            integrity[:-2] + "!!",
            " " + integrity,
            integrity + " ",
            "sha512-../outside",
        ):
            with self.assertRaises(stager.ClosureStagingError):
                stager._parse_sri(invalid)

        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory) / "cache"
            cache.mkdir()
            safe_name = (
                "sha512-"
                + base64.b64encode(digest)
                .decode("ascii")
                .replace("+", "_")
                .replace("/", "_")
                .replace("=", "_")
                + ".tgz"
            )
            (cache / safe_name).write_bytes(payload)
            record = {
                "integrity": integrity,
                "resolved": "https://example.test/fixture.tgz",
            }
            with mock.patch.object(stager.urllib.request, "urlopen") as urlopen:
                self.assertEqual(payload, stager._cache_tarball(record, cache))
                urlopen.assert_not_called()
            self.assertEqual([safe_name], [path.name for path in cache.iterdir()])

            invalid_cache = Path(directory) / "invalid-cache"
            with self.assertRaises(stager.ClosureStagingError):
                stager._cache_tarball(
                    {"integrity": "sha512-../escape", "resolved": record["resolved"]},
                    invalid_cache,
                )
            self.assertFalse(invalid_cache.exists())
            self.assertFalse((Path(directory) / "escape.tgz").exists())

    def test_cache_requires_https_after_redirect_and_uses_exclusive_files(self):
        payload = b"fixture tarball"
        integrity = "sha512-" + base64.b64encode(hashlib.sha512(payload).digest()).decode("ascii")
        record = {
            "integrity": integrity,
            "resolved": "https://example.test/fixture.tgz",
        }

        class Response:
            def __init__(self, final_url):
                self.final_url = final_url

            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

            def geturl(self):
                return self.final_url

            def read(self, size=-1):
                return payload if size < 0 else payload[:size]

        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory) / "cache"
            with mock.patch.object(
                stager.urllib.request,
                "urlopen",
                return_value=Response("http://example.test/redirected.tgz"),
            ):
                with self.assertRaises(stager.ClosureStagingError):
                    stager._cache_tarball(record, cache)
            self.assertFalse(list(cache.glob("*.tgz")))

            with mock.patch.object(
                stager.urllib.request,
                "urlopen",
                return_value=Response("https://registry.example/fixture.tgz"),
            ):
                self.assertEqual(payload, stager._cache_tarball(record, cache))
            self.assertEqual(1, len(list(cache.glob("*.tgz"))))

            racing_cache = Path(directory) / "racing-cache"

            def publish_concurrent_winner(source, target, **_):
                Path(target).write_bytes(Path(source).read_bytes())
                raise FileExistsError(target)

            with mock.patch.object(
                stager.urllib.request,
                "urlopen",
                return_value=Response("https://registry.example/fixture.tgz"),
            ), mock.patch.object(stager.os, "link", side_effect=publish_concurrent_winner):
                self.assertEqual(payload, stager._cache_tarball(record, racing_cache))
            self.assertEqual(1, len(list(racing_cache.glob("*.tgz"))))

            cache_link = Path(directory) / "cache-link"
            cache_link.symlink_to(cache, target_is_directory=True)
            with self.assertRaises(stager.ClosureStagingError):
                stager._cache_tarball(record, cache_link)

    def test_truncated_or_invalid_gzip_has_stable_error_and_no_partial_target(self):
        payload = self._package_tarball(
            {"package.json": '{"name":"fixture","version":"1"}', "index.js": "export {};"}
        )
        for broken in (b"not gzip", payload[:-32]):
            with tempfile.TemporaryDirectory() as directory:
                target = Path(directory) / "package"
                with self.assertRaises(stager.ClosureStagingError):
                    stager._extract_package(broken, target, "1", "fixture")
                self.assertFalse(target.exists())

    def test_tarball_and_expansion_resource_limits_are_enforced(self):
        payload = self._package_tarball(
            {"package.json": '{"name":"fixture","version":"1"}', "index.js": "x"}
        )
        integrity = "sha512-" + base64.b64encode(
            hashlib.sha512(payload).digest()
        ).decode()
        with tempfile.TemporaryDirectory() as directory:
            cache = Path(directory) / "cache"
            cache.mkdir()
            cache_path = cache / f"sha512-{hashlib.sha512(payload).hexdigest()}.tgz"
            cache_path.write_bytes(payload)
            record = {
                "integrity": integrity,
                "resolved": "https://example.test/fixture.tgz",
            }
            with mock.patch.object(stager, "MAX_TARBALL_BYTES", len(payload) - 1):
                with self.assertRaises(stager.ClosureStagingError):
                    stager._cache_tarball(record, cache)

            target = Path(directory) / "member-limit"
            with mock.patch.object(stager, "MAX_ARCHIVE_MEMBERS", 1):
                with self.assertRaises(stager.ClosureStagingError):
                    stager._extract_package(payload, target, "1", "fixture")
            self.assertFalse(target.exists())

            target = Path(directory) / "expanded-limit"
            with mock.patch.object(stager, "MAX_PACKAGE_EXPANDED_BYTES", 1):
                with self.assertRaises(stager.ClosureStagingError):
                    stager._extract_package(payload, target, "1", "fixture")
            self.assertFalse(target.exists())

            target = Path(directory) / "closure-limit"
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(
                    payload,
                    target,
                    "1",
                    "fixture",
                    remaining_closure_bytes=1,
                )
            self.assertFalse(target.exists())

    def test_archive_rejects_symlinks_and_parent_tarball_cannot_claim_nested_target(self):
        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w:gz") as archive:
            metadata = b'{"name":"outer","version":"1"}'
            info = tarfile.TarInfo("package/package.json")
            info.size = len(metadata)
            archive.addfile(info, io.BytesIO(metadata))
            link = tarfile.TarInfo("package/link")
            link.type = tarfile.SYMTYPE
            link.linkname = "/outside"
            archive.addfile(link)
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "outer"
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(payload.getvalue(), target, "1", "outer")
            self.assertFalse(target.exists())

        outer_payload = self._package_tarball(
            {
                "package.json": '{"name":"outer","version":"1"}',
                "node_modules/inner/package.json": '{"name":"inner","version":"1"}',
            }
        )
        inner_payload = self._package_tarball(
            {"package.json": '{"name":"inner","version":"1"}'}
        )
        with tempfile.TemporaryDirectory() as directory:
            outer = Path(directory) / "node_modules" / "outer"
            inner = outer / "node_modules" / "inner"
            stager._extract_package(outer_payload, outer, "1", "outer")
            with self.assertRaises(stager.ClosureStagingError):
                stager._extract_package(inner_payload, inner, "1", "inner")
            self.assertEqual("inner", json.loads((inner / "package.json").read_text())["name"])

    def test_lock_rejects_case_aliases_and_optional_peer_is_excludable(self):
        root = stager.ROOT_PACKAGES[0]
        shim = stager.ROOT_PACKAGES[1]
        peer = "node_modules/optional-peer"
        records = {
            root: self._record(
                "@bytecodealliance/jco",
                "1",
                peer_dependencies={"optional-peer": "*"},
            ),
            shim: self._record("@bytecodealliance/preview2-shim", "1"),
            peer: self._record("optional-peer", "1"),
        }
        records[root]["peerDependenciesMeta"] = {"optional-peer": {"optional": True}}
        lock = {"lockfileVersion": 3, "packages": records}
        policy = self._policy(
            lock,
            [root, shim],
            [{"path": peer, "reason": "optional runtime branch", "evidence": "fixture"}],
        )
        self.assertEqual({root, shim}, set(stager.select_locked_closure(lock, policy)))

        records[root]["peerDependenciesMeta"] = {"optional-peer": {"optional": "yes"}}
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(lock, policy)

        alias_records = {
            root: self._record("@bytecodealliance/jco", "1"),
            shim: self._record("@bytecodealliance/preview2-shim", "1"),
            "node_modules/Foo": self._record("Foo", "1"),
            "node_modules/foo": self._record("foo", "1"),
        }
        alias_lock = {"lockfileVersion": 3, "packages": alias_records}
        with self.assertRaises(stager.ClosureStagingError):
            stager.select_locked_closure(
                alias_lock, self._policy(alias_lock, list(alias_records), [])
            )

    def test_stage_main_preserves_generation_symlinks_for_rejection(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            outside = Path(directory) / "outside"
            fixture["generation"].symlink_to(outside, target_is_directory=True)
            result = stager.main(
                [
                    "--lock", str(fixture["lock"]),
                    "--policy", str(fixture["policy"]),
                    "--generation-root", str(fixture["generation"]),
                    "--cache", str(fixture["cache"]),
                ]
            )
            self.assertEqual(2, result)
            self.assertTrue(fixture["generation"].is_symlink())

    def test_stage_rejects_lock_hash_drift_without_publishing_generation(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            policy = json.loads(fixture["policy"].read_text())
            policy["lockSha256"] = hashlib.sha256(b"different lock").hexdigest()
            fixture["policy"].write_text(json.dumps(policy) + "\n")
            with self.assertRaises(stager.ClosureStagingError):
                self._run_stage(fixture)
            self.assertFalse(fixture["generation"].exists())

    def test_stage_failure_never_publishes_or_deletes_a_requested_generation(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            with mock.patch.object(
                stager,
                "_write_notices",
                side_effect=OSError("injected generation failure"),
            ):
                with self.assertRaises(OSError):
                    self._run_stage(fixture)
            self.assertFalse(fixture["generation"].exists())
            self.assertEqual(
                [],
                list(fixture["generation"].parent.glob(".generation.staging-*")),
            )

            fixture["generation"].mkdir()
            sentinel = fixture["generation"] / "owned-by-another-process"
            sentinel.write_text("preserve")
            with self.assertRaises(stager.ClosureStagingError):
                self._run_stage(fixture)
            self.assertEqual("preserve", sentinel.read_text())

    def test_atomic_publication_failure_leaves_no_root_and_can_retry(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            with mock.patch.object(
                stager.os,
                "rename",
                side_effect=OSError("injected atomic publication failure"),
            ):
                with self.assertRaises(OSError):
                    self._run_stage(fixture)

            self.assertFalse(fixture["generation"].exists())
            self.assertEqual(
                [],
                list(fixture["generation"].parent.glob(".generation.staging-*")),
            )
            self.assertEqual(2, self._run_stage(fixture))
            self.assertTrue(fixture["marker"].is_file())

    def test_stage_rejects_cache_and_generation_path_overlap(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            fixture["cache"] = fixture["generation"] / "cache"
            with self.assertRaises(stager.ClosureStagingError):
                self._run_stage(fixture)
            self.assertFalse(fixture["generation"].exists())

            self.assertTrue(
                stager._paths_overlap(
                    Path(directory) / "Generation",
                    Path(directory) / "generation" / "cache",
                )
            )

            fixture = self._stage_fixture(Path(directory) / "inverse")
            fixture["generation"] = fixture["cache"] / "generation"
            fixture["canonical"] = (
                fixture["generation"] / stager.GENERATION_PAYLOAD_NAME
            )
            fixture["marker"] = (
                fixture["generation"] / stager.GENERATION_MARKER_NAME
            )
            with self.assertRaises(stager.ClosureStagingError):
                self._run_stage(fixture)
            self.assertFalse(fixture["generation"].exists())

    def test_stage_enforces_the_closure_wide_expansion_limit(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            with mock.patch.object(stager, "MAX_CLOSURE_EXPANDED_BYTES", 1):
                with self.assertRaises(stager.ClosureStagingError):
                    self._run_stage(fixture)
            self.assertFalse(fixture["generation"].exists())

    def test_stage_snapshots_lock_bytes_and_generated_metadata_is_lf_deterministic(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            original_lock = fixture["lock"].read_bytes()
            original_select = stager.select_locked_closure

            def mutate_source_after_snapshot(lock, policy):
                selected = original_select(lock, policy)
                fixture["lock"].write_bytes(b"changed after snapshot")
                return selected

            with mock.patch.object(
                stager, "select_locked_closure", side_effect=mutate_source_after_snapshot
            ):
                self._run_stage(fixture)
            self.assertEqual(
                original_lock, (fixture["canonical"] / "package-lock.json").read_bytes()
            )

            integrity_path = fixture["canonical"] / "closure-integrity.json"
            first_integrity = integrity_path.read_bytes()
            stager._write_integrity(fixture["canonical"], integrity_path)
            self.assertEqual(first_integrity, integrity_path.read_bytes())

            pack_path = fixture["canonical"] / "closure-pack-items.props"
            first_pack = pack_path.read_bytes()
            stager._write_pack_items(fixture["canonical"], pack_path, "tools/jco")
            self.assertEqual(first_pack, pack_path.read_bytes())
            self.assertNotIn(b"\\r", first_integrity + first_pack)
            self.assertNotIn(b"\\", first_pack)
            self.assertIn(b"<NoDefaultExcludes>true</NoDefaultExcludes>", first_pack)
            self.assertIn(
                b'PackagePath="tools/jco/node_modules/@bytecodealliance/jco/"',
                first_pack,
            )

    def test_stage_publishes_one_complete_immutable_generation(self):
        with tempfile.TemporaryDirectory() as directory:
            fixture = self._stage_fixture(Path(directory))
            count = self._run_stage(fixture)
            self.assertEqual(2, count)
            canonical = fixture["canonical"]
            self.assertTrue(
                (canonical / "node_modules/@bytecodealliance/jco/dist/jco.js").is_file()
            )
            self.assertEqual(
                fixture["lock"].read_bytes(),
                (canonical / "package-lock.json").read_bytes(),
            )
            self.assertEqual(
                fixture["policy"].read_bytes(),
                (canonical / "closure-policy.json").read_bytes(),
            )
            self.assertNotIn(
                str(fixture["generation"]),
                (canonical / "closure-pack-items.props").read_text(),
            )
            marker = json.loads(fixture["marker"].read_text())
            self.assertEqual("complete", marker["state"])
            self.assertEqual("payload", marker["canonicalRoot"])
            with self.assertRaises(stager.ClosureStagingError):
                self._run_stage(fixture)

    @classmethod
    def _stage_fixture(cls, root):
        lock_path = root / "package-lock.json"
        policy_path = root / "policy.json"
        cache = root / "cache"
        generation = root / "generation"
        canonical = generation / stager.GENERATION_PAYLOAD_NAME

        package_paths = {
            stager.ROOT_PACKAGES[0]: ("@bytecodealliance/jco", "1.28.1", True),
            stager.ROOT_PACKAGES[1]: ("@bytecodealliance/preview2-shim", "0.24.1", False),
        }
        records = {}
        for path, (name, version, jco) in package_paths.items():
            files = {"package.json": json.dumps({"name": name, "version": version})}
            if jco:
                files["dist/jco.js"] = "#!/usr/bin/env node\n"
                files["LICENSE"] = "license\n"
            payload = cls._package_tarball(files)
            integrity = "sha512-" + base64.b64encode(hashlib.sha512(payload).digest()).decode()
            cache.mkdir(parents=True, exist_ok=True)
            digest = hashlib.sha512(payload).hexdigest()
            (cache / f"sha512-{digest}.tgz").write_bytes(payload)
            records[path] = {
                "name": name,
                "version": version,
                "resolved": f"https://example/{name}-{version}.tgz",
                "integrity": integrity,
            }

        lock = {"name": "fixture", "lockfileVersion": 3, "packages": records}
        policy = cls._policy(lock, list(records), [])
        lock_bytes = (json.dumps(lock) + "\n").encode()
        lock_path.write_bytes(lock_bytes)
        policy["lockSha256"] = hashlib.sha256(lock_bytes).hexdigest()
        policy_path.write_text(json.dumps(policy) + "\n", encoding="utf-8")
        return {
            "lock": lock_path,
            "policy": policy_path,
            "cache": cache,
            "generation": generation,
            "canonical": canonical,
            "marker": generation / stager.GENERATION_MARKER_NAME,
        }

    @staticmethod
    def _run_stage(fixture):
        return stager.stage(
            fixture["lock"],
            fixture["policy"],
            fixture["generation"],
            fixture["cache"],
        )

    @staticmethod
    def _record(name, version, optional_dependencies=None, peer_dependencies=None):
        payload = f"{name}:{version}".encode()
        integrity = "sha512-" + base64.b64encode(hashlib.sha512(payload).digest()).decode()
        return {
            "name": name,
            "version": version,
            "resolved": f"https://example/{name}-{version}.tgz",
            "integrity": integrity,
            "optionalDependencies": optional_dependencies or {},
            "peerDependencies": peer_dependencies or {},
        }

    @classmethod
    def _policy(cls, lock, selected, excluded):
        packages = lock["packages"]
        lock.setdefault("name", "fixture")
        packages.setdefault(
            "",
            {
                "name": lock["name"],
                "devDependencies": {
                    "@bytecodealliance/jco": packages[stager.ROOT_PACKAGES[0]]["version"],
                    "@bytecodealliance/preview2-shim": packages[
                        stager.ROOT_PACKAGES[1]
                    ]["version"],
                },
            },
        )
        names = {path: lock["packages"][path].get("name", path.rsplit("/", 1)[-1]) for path in lock["packages"]}
        selected_entries = []
        for path in selected:
            record = lock["packages"][path]
            selected_entries.append({
                "path": path,
                "name": names[path],
                "version": record["version"],
                "resolved": record["resolved"],
                "integrity": record["integrity"],
            })
        policy = {
            "schemaVersion": "1",
            "contract": "jco-transpile-only",
            "roots": list(stager.ROOT_PACKAGES),
            "entryPoint": f"{stager.ROOT_PACKAGES[0]}/dist/jco.js",
            "requiredCommands": ["transpile"],
            "acceptedInvocation": list(stager.ACCEPTED_INVOCATION),
            "compilerClosure": [
                path for path in selected if path != stager.ROOT_PACKAGES[1]
            ],
            "runtimeShimClosure": [
                stager.ROOT_PACKAGES[1]
                for path in selected
                if path == stager.ROOT_PACKAGES[1]
            ],
            "selectedPackagePaths": selected_entries,
            "excludedPackagePaths": excluded,
        }
        lock_bytes = json.dumps(lock).encode()
        policy["lockSha256"] = hashlib.sha256(lock_bytes).hexdigest()
        return policy

    @staticmethod
    def _package_tarball(files):
        payload = io.BytesIO()
        with tarfile.open(fileobj=payload, mode="w:gz") as archive:
            for name, content in sorted(files.items()):
                data = content.encode()
                info = tarfile.TarInfo(f"package/{name}")
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
        return payload.getvalue()


if __name__ == "__main__":
    unittest.main()
