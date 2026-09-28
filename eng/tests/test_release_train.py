from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


SCRIPT = Path(__file__).parents[1] / "release-train.py"
SPEC = importlib.util.spec_from_file_location("release_train", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = Path(
    os.environ.get(
        "NETWASM_RELEASE_WORKFLOW_PATH",
        ROOT / ".github" / "workflows" / "release.yml",
    )
)


def workflow_step(document: str, name: str) -> str:
    marker = f"      - name: {name}\n"
    start = document.index(marker)
    end = document.find("\n      - name: ", start + len(marker))
    return document[start:] if end < 0 else document[start:end]


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
                self.tag, "v0.5.0", "netwasm-release-train-0.5.0",
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
                self.tag, "v0.5.0", "netwasm-release-train-0.5.0",
            )
        with self.assertRaisesRegex(ValueError, "coordinates"):
            MODULE.verify_train_identity(
                train, "zion-sati/NetWasm", self.commit, self.tag,
                self.tag, "v0.5.0", "unexpected-artifact",
            )

    def test_rejects_unsafe_and_unexpected_bundle_entries(self) -> None:
        path = self.root / "unsafe.zip"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("../escape", b"bad")
        with self.assertRaisesRegex(ValueError, "unsafe"):
            MODULE.inspect_bundle(path)

    def test_coordinated_train_binds_preparation_infrastructure_and_attempt(self) -> None:
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
        MODULE.create_bundle(
            stable_manifest_path, stable_receipt_path, self.packages, stable
        )
        toolchain = self.root / "toolchain.json"
        toolchain.write_text('{"emscripten":"6.0.7"}\n')

        train = MODULE.create_train_manifest(
            repository="zion-sati/NetWasm",
            source_commit=self.commit,
            producing_tag=self.tag,
            preview_tag=self.tag,
            stable_tag="v0.5.0",
            run_id="42",
            artifact_name="netwasm-release-train-0.5.0-42-3",
            preview_bundle=preview,
            stable_bundle=stable,
            toolchain=toolchain,
            preparation_sha256="1" * 64,
            infrastructure_commit="2" * 40,
            run_attempt="3",
        )

        self.assertEqual(2, train["schemaVersion"])
        self.assertEqual("1" * 64, train["preparationSha256"])
        self.assertEqual("2" * 40, train["infrastructureCommit"])
        self.assertEqual("3", train["workflowRunAttempt"])
        MODULE.validate_train_structure(train)
        MODULE.verify_train(
            train, preview, "preview", "zion-sati/NetWasm", self.commit,
            self.tag,
        )
        changed = dict(train)
        changed["preparationSha256"] = "3" * 63
        with self.assertRaisesRegex(ValueError, "preparation digest"):
            MODULE.validate_train_structure(changed)

    def test_identity_command_writes_exact_producer_coordinates(self) -> None:
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
        MODULE.create_bundle(
            stable_manifest_path, stable_receipt_path, self.packages, stable
        )
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
        train_path = self.root / "train.json"
        train_path.write_text(json.dumps(train))
        output = self.root / "github-output"

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "identity",
                "--train", str(train_path),
                "--repository", "zion-sati/NetWasm",
                "--source-commit", self.commit,
                "--producing-tag", self.tag,
                "--preview-tag", self.tag,
                "--stable-tag", "v0.5.0",
                "--artifact-name", "netwasm-release-train-0.5.0",
                "--github-output", str(output),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual(
            "producing_run_id=42\nartifact_name=netwasm-release-train-0.5.0\n",
            output.read_text(),
        )



class ReleaseTrainStateTests(unittest.TestCase):
    def test_first_preview_builds_when_no_train_is_bound(self) -> None:
        self.assertEqual("build", MODULE.effective_mode("build", 0))

    def test_preview_retry_resumes_when_train_is_bound(self) -> None:
        self.assertEqual("resume", MODULE.effective_mode("build", 1))

    def test_stable_promotes_when_preview_train_is_bound(self) -> None:
        self.assertEqual("promote", MODULE.effective_mode("promote", 1))

    def test_stable_fails_closed_without_preview_train(self) -> None:
        with self.assertRaisesRegex(ValueError, "requires the retained preview"):
            MODULE.effective_mode("promote", 0)

    def test_duplicate_manifest_identity_is_rejected(self) -> None:
        with self.assertRaisesRegex(ValueError, "ambiguous"):
            MODULE.effective_mode("build", 2)

    def test_state_command_writes_effective_mode(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "github-output"
            completed = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPT),
                    "state",
                    "--resolved-mode", "build",
                    "--retained-manifest-count", "1",
                    "--github-output", str(output),
                ],
                capture_output=True,
                text=True,
                check=False,
            )

            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertEqual("mode=resume\n", output.read_text(encoding="utf-8"))


class ReleasePreparationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.commits = {
            "zion-sati/NetWasm": "a" * 40,
            "zion-sati/TUnit-NetWasm": "b" * 40,
            "zion-sati/NetWasm.Libraries": "c" * 40,
            "zion-sati/NetWasm.Playground": "d" * 40,
            "zion-sati/netwasm.com": "e" * 40,
        }
        release_ids = iter(range(101, 108))
        stages = []
        for name, repository, workflow, upstream, _ in MODULE.PREPARATION_STAGES:
            source = self.commits[repository]
            website = name == "website"
            stages.append({
                "name": name,
                "repository": repository,
                "workflow": workflow,
                "sourceCommit": source,
                "infrastructureCommit": "f" * 40,
                "workflowCommit": source,
                "workflowRef": "main" if website else (
                    MODULE.expected_stage_ref(name, "0.5.0")
                ),
                "ref": source if website else MODULE.expected_stage_ref(name, "0.5.0"),
                "releaseId": None if website else next(release_ids),
                "prerelease": name.endswith("-preview"),
                "upstreamStages": list(upstream),
            })
        self.value = {
            "schemaVersion": 1,
            "version": "0.5.0",
            "stages": stages,
            "policy": {
                "publicationReceiptSchemaVersion": 1,
                "completionStage": "website",
            },
        }
        self.path = self.root / "preparation.json"
        self.path.write_text(json.dumps(self.value, indent=2) + "\n")

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_complete_preparation_is_exact_byte_digest_bound(self) -> None:
        value, digest = MODULE.read_preparation(self.path)

        self.assertEqual(hashlib.sha256(self.path.read_bytes()).hexdigest(), digest)
        self.assertEqual(
            "netwasm-v0.5.0-preview.1",
            MODULE.preparation_stage(value, "tunit-preview")["ref"],
        )

    def test_rejects_wrong_repository_workflow_order_or_upstream_graph(self) -> None:
        mutations = (
            ("repository", "zion-sati/Other"),
            ("workflow", ".github/workflows/other.yml"),
            ("upstreamStages", []),
        )
        for field, value in mutations:
            with self.subTest(field=field):
                changed = json.loads(json.dumps(self.value))
                changed["stages"][4][field] = value
                with self.assertRaisesRegex(ValueError, "stage identity"):
                    MODULE.validate_preparation(changed)

    def test_rejects_version_ref_and_same_repository_source_mismatch(self) -> None:
        wrong_ref = json.loads(json.dumps(self.value))
        wrong_ref["stages"][2]["ref"] = "netwasm-v0.5.1-preview.1"
        with self.assertRaisesRegex(ValueError, "release coordinates"):
            MODULE.validate_preparation(wrong_ref)

        different_source = json.loads(json.dumps(self.value))
        different_source["stages"][1]["sourceCommit"] = "1" * 40
        with self.assertRaisesRegex(ValueError, "different source or infrastructure"):
            MODULE.validate_preparation(different_source)

        different_infrastructure = json.loads(json.dumps(self.value))
        different_infrastructure["stages"][1]["infrastructureCommit"] = "1" * 40
        with self.assertRaisesRegex(ValueError, "different source or infrastructure"):
            MODULE.validate_preparation(different_infrastructure)

        invalid_workflow_ref = json.loads(json.dumps(self.value))
        invalid_workflow_ref["stages"][0]["workflowRef"] = "main\nrepository=other"
        with self.assertRaisesRegex(ValueError, "workflowRef"):
            MODULE.validate_preparation(invalid_workflow_ref)

    def test_preparation_command_outputs_only_verified_coordinates(self) -> None:
        output = self.root / "github-output"

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "preparation",
                "--manifest", str(self.path),
                "--stage", "libraries-preview",
                "--github-output", str(output),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stderr)
        outputs = dict(
            line.split("=", 1)
            for line in output.read_text(encoding="utf-8").splitlines()
        )
        self.assertEqual("0.5.0", outputs["version"])
        self.assertEqual(
            "zion-sati/NetWasm.Libraries", outputs["repository"]
        )
        self.assertEqual("v0.5.0-preview.1", outputs["ref"])
        self.assertEqual(
            hashlib.sha256(self.path.read_bytes()).hexdigest(),
            outputs["preparation_sha256"],
        )

    def test_publication_receipts_bind_versions_packages_and_upstream_coordinates(self) -> None:
        preparation, digest = MODULE.read_preparation(self.path)
        preview = self.publication_receipt(
            preparation,
            digest,
            "core-preview",
            "0.5.0-preview.1",
            [],
        )
        MODULE.validate_publication_receipt(
            preview,
            preparation,
            digest,
            "core-preview",
        )

        wrong_version = json.loads(json.dumps(preview))
        wrong_version["version"] = "0.5.0"
        with self.assertRaisesRegex(ValueError, "approved stage"):
            MODULE.validate_publication_receipt(
                wrong_version,
                preparation,
                digest,
                "core-preview",
            )

        stable = self.publication_receipt(
            preparation,
            digest,
            "core-stable",
            "0.5.0",
            [{
                "stage": "core-preview",
                "repository": "zion-sati/NetWasm",
                "version": "0.5.0-preview.1",
                "releaseTag": "v0.5.0-preview.1",
                "sha256": "6" * 64,
            }],
        )
        MODULE.validate_publication_receipt(
            stable,
            preparation,
            digest,
            "core-stable",
        )
        stable["upstreamReceipts"][0]["repository"] = "zion-sati/Other"
        with self.assertRaisesRegex(ValueError, "upstream coordinates"):
            MODULE.validate_publication_receipt(
                stable,
                preparation,
                digest,
                "core-stable",
            )

    def test_create_publication_receipt_binds_stable_to_preview_train(self) -> None:
        train_path, feed_path = self.write_candidate_inputs("core-stable")
        preparation, digest = MODULE.read_preparation(self.path)
        train = json.loads(train_path.read_text())
        preview = self.publication_receipt(
            preparation,
            digest,
            "core-preview",
            "0.5.0-preview.1",
            [],
        )
        preview["candidate"].update({
            "trainManifestSha256": MODULE.sha256(train_path),
            "producingRunId": train["workflowRunId"],
            "producingRunAttempt": train["workflowRunAttempt"],
            "artifactName": train["artifactName"],
        })
        preview_path = self.root / "core-preview-receipt.json"
        preview_path.write_text(json.dumps(preview))

        result = MODULE.create_publication_receipt(
            preparation_path=self.path,
            stage_name="core-stable",
            train_path=train_path,
            feed_receipt_path=feed_path,
            publication_run_id="99",
            publication_run_attempt="1",
            upstream_receipt_paths=[preview_path],
            upstream_receipt_sha256=[MODULE.sha256(preview_path)],
        )
        self.assertEqual("PASS", result["status"])

        for field, value in (
            ("trainManifestSha256", "8" * 64),
            ("producingRunId", "999"),
            ("producingRunAttempt", "9"),
            ("artifactName", "other-train"),
        ):
            with self.subTest(field=field):
                changed = json.loads(preview_path.read_text())
                changed["candidate"][field] = value
                preview_path.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError, "retained preview train"):
                    MODULE.create_publication_receipt(
                        preparation_path=self.path,
                        stage_name="core-stable",
                        train_path=train_path,
                        feed_receipt_path=feed_path,
                        publication_run_id="99",
                        publication_run_attempt="1",
                        upstream_receipt_paths=[preview_path],
                        upstream_receipt_sha256=[MODULE.sha256(preview_path)],
                    )
                preview_path.write_text(json.dumps(preview))

    def test_create_publication_receipt_rejects_malformed_candidates(self) -> None:
        train_path, feed_path = self.write_candidate_inputs("core-preview")
        baseline = json.loads(train_path.read_text())
        mutations = {
            "nested source": lambda value: value["preview"].__setitem__(
                "sourceCommit", "9" * 40
            ),
            "producing tag": lambda value: value["stable"].__setitem__(
                "producingReleaseTag", "v0.4.9-preview.1"
            ),
            "missing preview": lambda value: value.__setitem__("preview", {}),
            "duplicate package": lambda value: value["stable"]["packages"].append(
                dict(value["stable"]["packages"][0])
            ),
            "wrong approved producer": lambda value: (
                value.__setitem__("producingReleaseTag", "v0.4.9-preview.1"),
                value["preview"].__setitem__(
                    "producingReleaseTag", "v0.4.9-preview.1"
                ),
                value["stable"].__setitem__(
                    "producingReleaseTag", "v0.4.9-preview.1"
                ),
            ),
            "wrong unselected tag": lambda value: value["stable"].__setitem__(
                "releaseTag", "v0.5.1"
            ),
        }
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                changed = json.loads(json.dumps(baseline))
                mutate(changed)
                train_path.write_text(json.dumps(changed))
                with self.assertRaisesRegex(
                    ValueError, "candidate|packages|approved preparation"
                ):
                    MODULE.create_publication_receipt(
                        preparation_path=self.path,
                        stage_name="core-preview",
                        train_path=train_path,
                        feed_receipt_path=feed_path,
                        publication_run_id="99",
                        publication_run_attempt="1",
                        upstream_receipt_paths=[],
                        upstream_receipt_sha256=[],
                    )

    def write_candidate_inputs(self, stage_name: str) -> tuple[Path, Path]:
        preparation, digest = MODULE.read_preparation(self.path)
        stage = MODULE.preparation_stage(preparation, stage_name)
        package = lambda version, digit: {
            "id": "NetWasm.Example",
            "version": version,
            "fileName": f"NetWasm.Example.{version}.nupkg",
            "size": 123,
            "sha256": digit * 64,
        }
        candidate = lambda version, tag, digit: {
            "version": version,
            "sourceCommit": stage["sourceCommit"],
            "producingReleaseTag": "v0.5.0-preview.1",
            "bundleFile": f"candidate-{digit}.zip",
            "bundleSize": 456,
            "bundleSha256": digit * 64,
            "manifestSha256": str(int(digit) + 1) * 64,
            "receiptSha256": str(int(digit) + 2) * 64,
            "packages": [package(version, str(int(digit) + 3))],
            "releaseTag": tag,
        }
        train = {
            "schemaVersion": 2,
            "repository": stage["repository"],
            "sourceCommit": stage["sourceCommit"],
            "producingReleaseTag": "v0.5.0-preview.1",
            "workflowRunId": "42",
            "workflowRunAttempt": "3",
            "artifactName": "netwasm-release-train-0.5.0-42-3",
            "toolchainSha256": "1" * 64,
            "nativeToolchain": {"emscripten": "6.0.7"},
            "preparationSha256": digest,
            "infrastructureCommit": stage["infrastructureCommit"],
            "preview": candidate("0.5.0-preview.1", "v0.5.0-preview.1", "2"),
            "stable": candidate("0.5.0", "v0.5.0", "5"),
        }
        train_path = self.root / f"{stage_name}-train.json"
        train_path.write_text(json.dumps(train))
        selected = train["preview" if stage_name.endswith("-preview") else "stable"]
        selected_package = selected["packages"][0]
        feed = {
            "schemaVersion": 1,
            "status": "PASS",
            "repository": stage["repository"],
            "releaseVersion": selected["version"],
            "releaseTag": stage["ref"],
            "sourceCommit": stage["sourceCommit"],
            "feed": "https://api.nuget.org/v3/index.json",
            "packages": [{
                "id": selected_package["id"],
                "version": selected["version"],
                "fileName": selected_package["fileName"],
                "candidateSha256": selected_package["sha256"],
                "normalizedPayloadSha256": "9" * 64,
            }],
        }
        feed_path = self.root / f"{stage_name}-feed.json"
        feed_path.write_text(json.dumps(feed))
        return train_path, feed_path

    @staticmethod
    def publication_receipt(
        preparation: dict[str, object],
        preparation_digest: str,
        stage_name: str,
        version: str,
        upstream: list[dict[str, object]],
    ) -> dict[str, object]:
        stage = MODULE.preparation_stage(preparation, stage_name)
        return {
            "schemaVersion": 1,
            "status": "PASS",
            "stage": stage_name,
            "repository": stage["repository"],
            "version": version,
            "releaseTag": stage["ref"],
            "sourceCommit": stage["sourceCommit"],
            "preparationSha256": preparation_digest,
            "candidate": {
                "trainManifestSha256": "1" * 64,
                "bundleSha256": "2" * 64,
                "packageReceiptSha256": "3" * 64,
                "producingRunId": "42",
                "producingRunAttempt": "1",
                "artifactName": "release-train",
            },
            "publication": {
                "workflow": stage["workflow"],
                "infrastructureCommit": stage["infrastructureCommit"],
                "runId": "43",
                "runAttempt": "1",
            },
            "feed": "https://api.nuget.org/v3/index.json",
            "packages": [{
                "id": "NetWasm.Example",
                "version": version,
                "fileName": f"NetWasm.Example.{version}.nupkg",
                "candidateSha256": "4" * 64,
                "normalizedPayloadSha256": "5" * 64,
            }],
            "upstreamReceipts": upstream,
        }


class ReleaseWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not WORKFLOW.is_file():
            raise unittest.SkipTest("Release workflow is unavailable in this checkout.")
        cls.document = WORKFLOW.read_text(encoding="utf-8")

    def test_stable_promotion_cannot_start_build_steps(self) -> None:
        for name in (
            "Install .NET SDK",
            "Read pinned Node.js version",
            "Install Node.js",
            "Install pinned native toolchain",
            "Build release packages",
            "Retain neutral release packages",
        ):
            self.assertIn(
                "if: steps.train-state.outputs.mode == 'build'",
                workflow_step(self.document, name),
            )
        self.assertIn(
            "  host-tools:\n    needs: build\n    if: needs.build.outputs.mode == 'build'",
            self.document,
        )

    def test_stable_candidate_is_bound_to_preview_release_and_producing_run(self) -> None:
        coordinates = workflow_step(
            self.document, "Resolve retained release train coordinates"
        )
        retained = workflow_step(
            self.document, "Resolve and verify retained candidate"
        )
        for contract in (
            'gh release download "$PREVIEW_TAG"',
            "release-train.py identity",
            '--artifact-name "$ARTIFACT_NAME"',
        ):
            self.assertIn(contract, coordinates)
        for contract in (
            'gh run download "${{ steps.retained-train.outputs.producing_run_id }}"',
            'cmp --silent "$train" "$downloaded/$ARTIFACT_NAME.json"',
            "release-train.py verify",
            '--channel "$channel"',
            "release-train.py extract",
            "verify-release-packages.py",
        ):
            self.assertIn(contract, retained)
        self.assertNotIn("build-packages", retained)
        self.assertNotIn("build-host-tools", retained)

    def test_preview_retry_selects_retained_preview_without_building(self) -> None:
        state = workflow_step(self.document, "Resolve release train state")
        retained = workflow_step(
            self.document, "Resolve and verify retained candidate"
        )
        self.assertIn("release-train.py state", state)
        self.assertIn("steps.train-state.outputs.mode", self.document)
        self.assertIn('if [[ "$RELEASE_MODE" == "resume" ]]; then', retained)
        self.assertIn("channel=preview", retained)
        self.assertIn('version="$PREVIEW_VERSION"', retained)
        self.assertIn('release_tag="$PREVIEW_TAG"', retained)
        self.assertIn("if: needs.build.outputs.mode != 'build'", self.document)

    def test_publication_has_preflight_and_two_parallelizable_waves(self) -> None:
        self.assertEqual(2, self.document.count("uses: NuGet/login@"))
        self.assertIn("--preflight", self.document)
        self.assertIn("--stage prerequisites", self.document)
        self.assertIn("--stage final", self.document)
        self.assertNotIn("--stage SDK", self.document)
        self.assertNotIn("--stage templates", self.document)
        self.assertIn("Retain release timing evidence", self.document)

    def test_coordinated_approval_precedes_nuget_credentials(self) -> None:
        approval = self.document.index("Verify coordinated publication approval")
        first_credential = self.document.index("Request credential for prerequisites")
        self.assertLess(approval, first_credential)
        self.assertIn(
            "release-train.py approval",
            workflow_step(self.document, "Verify coordinated publication approval"),
        )


if __name__ == "__main__":
    unittest.main()
