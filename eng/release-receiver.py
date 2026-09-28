#!/usr/bin/env python3
"""Verify a coordinated package-stage dispatch before publication begins."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path


SCRIPT_ROOT = Path(__file__).resolve().parent


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TRAIN = load("release_train", SCRIPT_ROOT / "release-train.py")
COORDINATOR = load("release_coordinator", SCRIPT_ROOT / "release-coordinator.py")


def read_preparation(path: Path, expected_digest: str) -> dict[str, object]:
    contents = path.read_bytes()
    digest = hashlib.sha256(contents).hexdigest()
    if digest != expected_digest:
        raise ValueError("Downloaded release preparation digest does not match dispatch.")
    value = json.loads(contents)
    if not isinstance(value, dict):
        raise ValueError("Release preparation must be an object.")
    return TRAIN.validate_preparation(value)


def validate_inputs(
    inputs: dict[str, object],
    preparation: dict[str, object],
    preparation_digest: str,
    repository: str,
    workflow_sha: str,
    workflow_ref: str,
) -> tuple[dict[str, object], list[dict[str, object]]]:
    fields = {
        "coordinated_stage", "preparation_sha256", "release_id", "release_ref",
        "source_commit", "infrastructure_commit", "coordinator_repository",
        "coordinator_run_id", "coordinator_run_attempt", "stage_identity",
        "dispatch_attempt_identity", "upstream_receipts",
    }
    if set(inputs) != fields or any(not isinstance(value, str) for value in inputs.values()):
        raise ValueError("Coordinated release inputs are invalid.")
    stage_name = str(inputs["coordinated_stage"])
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}:
        raise ValueError("Receiver accepts package publication stages only.")
    stage = TRAIN.preparation_stage(preparation, stage_name)
    if stage.get("repository") != repository:
        raise ValueError("Coordinated stage targets a different repository.")
    expected = {
        "preparation_sha256": preparation_digest,
        "release_id": str(stage["releaseId"]),
        "release_ref": str(stage["ref"]),
        "source_commit": str(stage["sourceCommit"]),
        "infrastructure_commit": str(stage["infrastructureCommit"]),
        "coordinator_repository": COORDINATOR.COORDINATOR_REPOSITORY,
        "stage_identity": COORDINATOR.stage_identity(preparation_digest, stage_name),
    }
    if any(inputs.get(name) != value for name, value in expected.items()):
        raise ValueError("Coordinated release inputs do not match preparation.")
    if workflow_sha != stage.get("workflowCommit") or workflow_ref != stage.get("workflowRef"):
        raise ValueError("Receiver workflow identity does not match preparation.")
    if inputs.get("dispatch_attempt_identity") != COORDINATOR.dispatch_attempt_identity(
        preparation_digest,
        stage_name,
        str(inputs["coordinator_run_id"]),
        str(inputs["coordinator_run_attempt"]),
    ):
        raise ValueError("Coordinated dispatch attempt identity is invalid.")
    try:
        upstream = json.loads(str(inputs["upstream_receipts"]))
    except json.JSONDecodeError as error:
        raise ValueError("Coordinated upstream receipts are invalid.") from error
    expected_names = stage.get("upstreamStages")
    if not isinstance(upstream, list) or not isinstance(expected_names, list):
        raise ValueError("Coordinated upstream receipts are invalid.")
    if [item.get("stage") for item in upstream if isinstance(item, dict)] != expected_names:
        raise ValueError("Coordinated upstream receipt stages do not match preparation.")
    for item in upstream:
        if not isinstance(item, dict) or set(item) != {"stage", "sha256", "fileName"}:
            raise ValueError("Coordinated upstream receipt coordinates are invalid.")
        file_name = item.get("fileName")
        digest = item.get("sha256")
        if (
            not isinstance(file_name, str)
            or not file_name
            or Path(file_name).name != file_name
            or not isinstance(digest, str)
            or TRAIN.SHA256.fullmatch(digest) is None
        ):
            raise ValueError("Coordinated upstream receipt coordinates are invalid.")
    return stage, upstream


def validate_actions_context(
    stage: dict[str, object],
    *,
    event_name: str,
    workflow_identity: str,
    workflow_sha: str,
    workflow_ref: str,
    actor_id: str,
    approved_actor_id: str,
) -> None:
    expected_prefix = f"{stage['repository']}/{stage['workflow']}@refs/"
    expected_suffixes = {
        f"heads/{stage['workflowRef']}", f"tags/{stage['workflowRef']}"
    }
    actual_suffix = (
        workflow_identity.removeprefix(expected_prefix)
        if workflow_identity.startswith(expected_prefix)
        else ""
    )
    if (
        event_name != "workflow_dispatch"
        or not workflow_identity.startswith(expected_prefix)
        or workflow_identity.count("@") != 1
        or actual_suffix not in expected_suffixes
        or workflow_sha != stage.get("workflowCommit")
        or workflow_ref != stage.get("workflowRef")
        or not actor_id.isdigit()
        or not approved_actor_id.isdigit()
        or actor_id != approved_actor_id
    ):
        raise ValueError("Receiver Actions context is not an approved coordinator dispatch.")


def validate_release(
    release: dict[str, object],
    stage: dict[str, object],
    tag_commit: str,
) -> None:
    if (
        release.get("id") != stage.get("releaseId")
        or release.get("tag_name") != stage.get("ref")
        or release.get("draft") is not False
        or release.get("prerelease") is not stage.get("prerelease")
        or tag_commit != stage.get("sourceCommit")
    ):
        raise ValueError("Published GitHub Release does not match preparation.")


def validate_upstream_receipts(
    upstream: list[dict[str, object]],
    directory: Path,
    preparation: dict[str, object],
    preparation_digest: str,
) -> list[tuple[Path, dict[str, object]]]:
    result = []
    for coordinate in upstream:
        path = directory / str(coordinate["fileName"])
        if not path.is_file() or TRAIN.sha256(path) != coordinate["sha256"]:
            raise ValueError(
                f"Upstream receipt bytes do not match dispatch: {coordinate['stage']}."
            )
        value = TRAIN.read_json(path)
        TRAIN.validate_publication_receipt(
            value, preparation, preparation_digest, str(coordinate["stage"])
        )
        result.append((path, value))
    return result


def validate_upstream_runs(
    receipts: list[tuple[Path, dict[str, object]]],
    run_directory: Path,
    preparation: dict[str, object],
    approved_actor_id: str,
) -> None:
    for _, receipt in receipts:
        stage_name = str(receipt["stage"])
        stage = TRAIN.preparation_stage(preparation, stage_name)
        publication = receipt.get("publication")
        if not isinstance(publication, dict):
            raise ValueError("Upstream publication coordinates are invalid.")
        run_path = run_directory / f"{stage_name}.json"
        if not run_path.is_file():
            raise ValueError(f"Upstream workflow metadata is missing: {stage_name}.")
        run = TRAIN.read_json(run_path)
        repository = run.get("repository")
        actor = run.get("actor")
        if (
            run.get("id") != int(str(publication.get("runId")))
            or run.get("run_attempt") != int(str(publication.get("runAttempt")))
            or run.get("event") != "workflow_dispatch"
            or run.get("path") != stage.get("workflow")
            or run.get("head_sha") != stage.get("workflowCommit")
            or run.get("head_branch") != stage.get("workflowRef")
            or run.get("status") != "completed"
            or run.get("conclusion") != "success"
            or not isinstance(repository, dict)
            or repository.get("full_name") != stage.get("repository")
            or not isinstance(actor, dict)
            or str(actor.get("id")) != approved_actor_id
        ):
            raise ValueError(
                f"Upstream publication workflow did not complete successfully: "
                f"{stage_name}."
            )


def verify_receiver(
    *,
    inputs_path: Path,
    preparation_path: Path,
    release_path: Path,
    upstream_directory: Path,
    upstream_run_directory: Path,
    repository: str,
    event_name: str,
    workflow_identity: str,
    workflow_sha: str,
    workflow_ref: str,
    actor_id: str,
    approved_actor_id: str,
    tag_commit: str,
) -> dict[str, object]:
    inputs = TRAIN.read_json(inputs_path)
    expected_digest = inputs.get("preparation_sha256")
    if not isinstance(expected_digest, str) or TRAIN.SHA256.fullmatch(expected_digest) is None:
        raise ValueError("Dispatch preparation digest is invalid.")
    preparation = read_preparation(preparation_path, expected_digest)
    stage, upstream = validate_inputs(
        inputs,
        preparation,
        expected_digest,
        repository,
        workflow_sha,
        workflow_ref,
    )
    validate_actions_context(
        stage,
        event_name=event_name,
        workflow_identity=workflow_identity,
        workflow_sha=workflow_sha,
        workflow_ref=workflow_ref,
        actor_id=actor_id,
        approved_actor_id=approved_actor_id,
    )
    release = TRAIN.read_json(release_path)
    validate_release(release, stage, tag_commit)
    receipts = validate_upstream_receipts(
        upstream, upstream_directory, preparation, expected_digest
    )
    validate_upstream_runs(
        receipts, upstream_run_directory, preparation, approved_actor_id
    )
    version = (
        f"{preparation['version']}-preview.1"
        if str(stage["name"]).endswith("-preview")
        else preparation["version"]
    )
    return {
        "status": "PASS",
        "stage": stage["name"],
        "version": version,
        "channel": "preview" if str(stage["name"]).endswith("-preview") else "stable",
        "preparationSha256": expected_digest,
        "sourceCommit": stage["sourceCommit"],
        "infrastructureCommit": stage["infrastructureCommit"],
        "releaseTag": stage["ref"],
        "releaseId": stage["releaseId"],
        "upstreamReceiptPaths": [str(path) for path, _ in receipts],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--preparation", type=Path, required=True)
    parser.add_argument("--release", type=Path, required=True)
    parser.add_argument("--upstream-directory", type=Path, required=True)
    parser.add_argument("--upstream-run-directory", type=Path, required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--event-name", required=True)
    parser.add_argument("--workflow-identity", required=True)
    parser.add_argument("--workflow-sha", required=True)
    parser.add_argument("--workflow-ref", required=True)
    parser.add_argument("--actor-id", required=True)
    parser.add_argument("--approved-actor-id", required=True)
    parser.add_argument("--tag-commit", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--github-output", type=Path)
    arguments = parser.parse_args()
    result = verify_receiver(
        inputs_path=arguments.inputs,
        preparation_path=arguments.preparation,
        release_path=arguments.release,
        upstream_directory=arguments.upstream_directory,
        upstream_run_directory=arguments.upstream_run_directory,
        repository=arguments.repository,
        event_name=arguments.event_name,
        workflow_identity=arguments.workflow_identity,
        workflow_sha=arguments.workflow_sha,
        workflow_ref=arguments.workflow_ref,
        actor_id=arguments.actor_id,
        approved_actor_id=arguments.approved_actor_id,
        tag_commit=arguments.tag_commit,
    )
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if arguments.github_output is not None:
        with arguments.github_output.open("a", encoding="utf-8") as stream:
            for field in (
                "stage", "version", "channel", "preparationSha256", "sourceCommit",
                "infrastructureCommit", "releaseTag", "releaseId",
            ):
                output_name = "".join(
                    ("_" + character.lower()) if character.isupper() else character
                    for character in field
                )
                stream.write(f"{output_name}={result[field]}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
