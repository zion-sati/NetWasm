from __future__ import annotations

import importlib.util
import json
import unittest
import urllib.request
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "release-preparation.py"
SPEC = importlib.util.spec_from_file_location("release_preparation", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class FakeStore:
    def __init__(self) -> None:
        self.values: dict[str, list[dict[str, object]]] = {
            repository: [] for repository in MODULE.REPOSITORIES[:-1]
        }
        self.assets: dict[tuple[str, int, str], bytes] = {}
        self.next_id = 100
        self.created = 0
        self.uploaded = 0
        self.tags: dict[tuple[str, str], str] = {}
        self.fail_create_at: int | None = None
        self.fail_upload_at: int | None = None

    def tag_commit(self, repository: str, tag: str) -> str | None:
        return self.tags.get((repository, tag))

    def verify_workflow_tree(self, repository: str, source_commit: str) -> None:
        return None

    def verify_workflow_ref(
        self, repository: str, workflow_ref: str, workflow_commit: str
    ) -> None:
        return None

    def releases(self, repository: str) -> list[dict[str, object]]:
        return self.values[repository]

    def create_draft(self, spec: dict[str, object]) -> dict[str, object]:
        if self.fail_create_at == self.created + 1:
            raise RuntimeError("interrupted create")
        self.next_id += 1
        self.created += 1
        value = {
            "id": self.next_id,
            "tag_name": spec["tag"],
            "target_commitish": spec["targetCommit"],
            "name": spec["title"],
            "draft": True,
            "prerelease": spec["prerelease"],
        }
        return value

    def release(self, repository: str, release_id: int) -> dict[str, object]:
        return next(
            release for release in self.values[repository]
            if release["id"] == release_id
        )

    def asset_bytes(
        self, repository: str, release_id: int, asset_name: str
    ) -> bytes | None:
        return self.assets.get((repository, release_id, asset_name))

    def upload_asset(
        self, repository: str, release_id: int, asset_name: str, contents: bytes
    ) -> None:
        if self.fail_upload_at == self.uploaded + 1:
            raise RuntimeError("interrupted upload")
        self.uploaded += 1
        self.assets[(repository, release_id, asset_name)] = contents


class ReleasePreparationTests(unittest.TestCase):
    def coordinates(self) -> dict[str, object]:
        repositories = {}
        for index, repository in enumerate(MODULE.REPOSITORIES, 1):
            repositories[repository] = {
                "sourceCommit": f"{index:x}" * 40,
                "infrastructureCommit": f"{index:x}" * 40,
                "workflowCommit": f"{index:x}" * 40,
                "workflowRef": "main",
            }
        return {
            "schemaVersion": 1,
            "version": "7.8.9",
            "repositories": repositories,
        }

    def test_one_version_derives_every_draft_and_final_preparation_tag(self) -> None:
        coordinates = self.coordinates()
        plan = MODULE.draft_plan(coordinates)
        release_ids = {
            name: index
            for index, name in enumerate(MODULE.PACKAGE_STAGE_NAMES, 101)
        }
        preparation = MODULE.create_preparation(coordinates, release_ids)

        self.assertEqual("7.8.9", plan["version"])
        self.assertEqual(
            [
                "v7.8.9-preview.1", "v7.8.9",
                "netwasm-v7.8.9-preview.1", "netwasm-v7.8.9",
                "v7.8.9-preview.1", "v7.8.9", "v7.8.9",
            ],
            [draft["tag"] for draft in plan["drafts"]],
        )
        self.assertEqual(
            [draft["tag"] for draft in plan["drafts"]],
            [stage["ref"] for stage in preparation["stages"][:-1]],
        )
        self.assertIsNone(preparation["stages"][-1]["releaseId"])

    def test_finalization_requires_one_unique_release_id_per_draft(self) -> None:
        coordinates = self.coordinates()
        incomplete = {
            name: index
            for index, name in enumerate(MODULE.PACKAGE_STAGE_NAMES[:-1], 101)
        }
        with self.assertRaisesRegex(ValueError, "Release ID set"):
            MODULE.create_preparation(coordinates, incomplete)

        duplicated = {
            name: 101 for name in MODULE.PACKAGE_STAGE_NAMES
        }
        with self.assertRaisesRegex(ValueError, "unique"):
            MODULE.create_preparation(coordinates, duplicated)

    def test_apply_is_repeatable_and_rejects_changed_preparation_asset(self) -> None:
        store = FakeStore()
        preparation, contents = MODULE.apply_preparation(self.coordinates(), store)

        self.assertEqual(7, store.created)
        self.assertEqual(7, store.uploaded)
        self.assertEqual(preparation, json.loads(contents))

        repeated, repeated_contents = MODULE.apply_preparation(
            self.coordinates(), store
        )
        self.assertEqual(preparation, repeated)
        self.assertEqual(contents, repeated_contents)
        self.assertEqual(7, store.created)
        self.assertEqual(7, store.uploaded)

        first = preparation["stages"][0]
        key = (
            first["repository"], first["releaseId"], MODULE.PREPARATION_ASSET
        )
        store.assets[key] = b"{}\n"
        with self.assertRaisesRegex(ValueError, "conflicting release-preparation"):
            MODULE.apply_preparation(self.coordinates(), store)

    def test_apply_recovers_after_interrupted_create_or_upload(self) -> None:
        for operation in ("create", "upload"):
            with self.subTest(operation=operation):
                store = FakeStore()
                if operation == "create":
                    store.fail_create_at = 3
                else:
                    store.fail_upload_at = 3
                with self.assertRaisesRegex(RuntimeError, "interrupted"):
                    MODULE.apply_preparation(self.coordinates(), store)
                store.fail_create_at = None
                store.fail_upload_at = None

                preparation, contents = MODULE.apply_preparation(
                    self.coordinates(), store
                )

                self.assertEqual(7, store.created)
                self.assertEqual(7, len(store.assets))
                self.assertEqual(preparation, json.loads(contents))

    def test_existing_tag_must_resolve_to_approved_source(self) -> None:
        store = FakeStore()
        coordinates = self.coordinates()
        first = MODULE.draft_plan(coordinates)["drafts"][0]
        store.tags[(first["repository"], first["tag"])] = "9" * 40
        with self.assertRaisesRegex(ValueError, "unapproved commit"):
            MODULE.apply_preparation(coordinates, store)

        store.tags[(first["repository"], first["tag"])] = first["targetCommit"]
        preparation, _ = MODULE.apply_preparation(coordinates, store)
        self.assertEqual("7.8.9", preparation["version"])

    def test_tag_resolution_handles_absent_lightweight_and_annotated_tags(self) -> None:
        store = MODULE.GitHubReleaseStore("token")
        values = iter((
            (None, {}),
            ({"object": {"type": "commit", "sha": "a" * 40}}, {}),
            ({"object": {"type": "tag", "sha": "b" * 40}}, {}),
            ({"object": {"type": "commit", "sha": "c" * 40}}, {}),
        ))
        store.request = lambda *args, **kwargs: next(values)
        self.assertIsNone(store.tag_commit("zion-sati/NetWasm", "absent"))
        self.assertEqual(
            "a" * 40, store.tag_commit("zion-sati/NetWasm", "lightweight")
        )
        self.assertEqual(
            "c" * 40, store.tag_commit("zion-sati/NetWasm", "annotated")
        )

    def test_redirects_keep_credentials_only_on_the_same_https_origin(self) -> None:
        handler = MODULE.CredentialSafeRedirectHandler()
        source = urllib.request.Request(
            "https://api.github.com/repos/example",
            headers={"Authorization": "Bearer secret"},
        )
        same = handler.redirect_request(
            source, None, 302, "Found", {}, "https://api.github.com/next"
        )
        cross = handler.redirect_request(
            source, None, 302, "Found", {}, "https://objects.example/asset"
        )
        self.assertEqual("Bearer secret", same.get_header("Authorization"))
        self.assertIsNone(cross.get_header("Authorization"))
        with self.assertRaisesRegex(ValueError, "downgrade"):
            handler.redirect_request(
                source, None, 302, "Found", {}, "http://api.github.com/next"
            )


if __name__ == "__main__":
    unittest.main()
