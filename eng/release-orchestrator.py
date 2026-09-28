#!/usr/bin/env python3
"""Run one resumable stage of the coordinated NetWasm release train."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import time
from typing import Callable
import urllib.parse
import zipfile


SCRIPT_ROOT = Path(__file__).resolve().parent


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


TRAIN = load("release_train", SCRIPT_ROOT / "release-train.py")
COORDINATOR = load("release_coordinator", SCRIPT_ROOT / "release-coordinator.py")
PREPARATION = load("release_preparation", SCRIPT_ROOT / "release-preparation.py")

CORE_REPOSITORY = "zion-sati/NetWasm"
CORE_PREVIEW_STAGE = "core-preview"
RECEIPT_FILE = "publication-receipt.json"
POLL_SECONDS = 15
STAGE_TIMEOUT_SECONDS = 50 * 60


def receipt_asset_name(stage_name: str) -> str:
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}:
        raise ValueError("Publication receipt stage is invalid.")
    return f"publication-receipt-{stage_name}.json"


class GitHubStore(PREPARATION.GitHubReleaseStore):
    def publish_release(
        self, repository: str, release_id: int
    ) -> dict[str, object]:
        value, _ = self.request(
            "PATCH",
            f"https://api.github.com/repos/{repository}/releases/{release_id}",
            payload={"draft": False},
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub release publication response is invalid.")
        return value

    def workflow_run(
        self, repository: str, run_id: str
    ) -> dict[str, object]:
        if not run_id.isdigit() or int(run_id) < 1:
            raise ValueError("Workflow run ID is invalid.")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/actions/runs/{run_id}",
        )
        if not isinstance(value, dict):
            raise ValueError("GitHub workflow run response is invalid.")
        return value

    def workflow_runs(
        self, repository: str, workflow: str, ref: str
    ) -> list[dict[str, object]]:
        workflow_name = Path(workflow).name
        if workflow != f".github/workflows/{workflow_name}":
            raise ValueError("Workflow path is invalid.")
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/actions/workflows/"
                f"{urllib.parse.quote(workflow_name, safe='')}/runs?"
                f"event=workflow_dispatch&branch={urllib.parse.quote(ref, safe='')}"
                f"&per_page=100&page={page}",
            )
            if not isinstance(value, dict) or not isinstance(
                value.get("workflow_runs"), list
            ):
                raise ValueError("GitHub workflow run listing is invalid.")
            runs = value["workflow_runs"]
            if any(not isinstance(item, dict) for item in runs):
                raise ValueError("GitHub workflow run listing is invalid.")
            result.extend(runs)
            if len(runs) < 100:
                return result
            page += 1

    def run_artifacts(
        self, repository: str, run_id: str
    ) -> list[dict[str, object]]:
        result = []
        page = 1
        while True:
            value, _ = self.request(
                "GET",
                f"https://api.github.com/repos/{repository}/actions/runs/"
                f"{run_id}/artifacts?per_page=100&page={page}",
            )
            if not isinstance(value, dict) or not isinstance(
                value.get("artifacts"), list
            ):
                raise ValueError("GitHub workflow artifact listing is invalid.")
            artifacts = value["artifacts"]
            if any(not isinstance(item, dict) for item in artifacts):
                raise ValueError("GitHub workflow artifact listing is invalid.")
            result.extend(artifacts)
            if len(artifacts) < 100:
                return result
            page += 1

    def artifact_bytes(self, repository: str, artifact_id: int) -> bytes:
        if artifact_id < 1:
            raise ValueError("Workflow artifact ID is invalid.")
        value, _ = self.request(
            "GET",
            f"https://api.github.com/repos/{repository}/actions/artifacts/"
            f"{artifact_id}/zip",
            accept="application/octet-stream",
        )
        if not isinstance(value, bytes):
            raise ValueError("GitHub workflow artifact response is invalid.")
        return value


def release_matches_stage(
    release: dict[str, object], stage: dict[str, object]
) -> bool:
    return (
        release.get("id") == stage.get("releaseId")
        and release.get("tag_name") == stage.get("ref")
        and release.get("prerelease") is stage.get("prerelease")
    )


def validate_published_release(
    store: GitHubStore,
    release: dict[str, object],
    stage: dict[str, object],
) -> None:
    if not release_matches_stage(release, stage) or release.get("draft") is not False:
        raise ValueError(f"Prepared release is not published: {stage['name']}.")
    if store.tag_commit(str(stage["repository"]), str(stage["ref"])) != stage.get(
        "sourceCommit"
    ):
        raise ValueError(f"Published release tag changed: {stage['name']}.")


def ensure_asset(
    store: GitHubStore,
    repository: str,
    release_id: int,
    name: str,
    contents: bytes,
) -> None:
    existing = store.asset_bytes(repository, release_id, name)
    if existing is None:
        store.upload_asset(repository, release_id, name, contents)
    elif existing != contents:
        raise ValueError(f"Release asset conflicts with approved bytes: {name}.")


def validate_run(
    run: dict[str, object],
    stage: dict[str, object],
    run_id: str,
    run_attempt: str,
    approved_actor_id: str,
    *,
    require_complete: bool,
) -> None:
    repository = run.get("repository")
    actor = run.get("actor")
    if (
        run.get("id") != int(run_id)
        or run.get("run_attempt") != int(run_attempt)
        or run.get("event") != "workflow_dispatch"
        or run.get("path") != stage.get("workflow")
        or run.get("head_sha") != stage.get("workflowCommit")
        or run.get("head_branch") != stage.get("workflowRef")
        or not isinstance(repository, dict)
        or repository.get("full_name") != stage.get("repository")
        or not isinstance(actor, dict)
        or str(actor.get("id")) != approved_actor_id
    ):
        raise ValueError(f"Workflow run identity is invalid: {stage['name']}.")
    if require_complete and (
        run.get("status") != "completed" or run.get("conclusion") != "success"
    ):
        raise ValueError(f"Workflow stage did not complete successfully: {stage['name']}.")


def validate_receipt_bytes(
    contents: bytes,
    preparation: dict[str, object],
    preparation_digest: str,
    stage_name: str,
) -> dict[str, object]:
    try:
        value = json.loads(contents)
    except json.JSONDecodeError as error:
        raise ValueError(f"Publication receipt is invalid JSON: {stage_name}.") from error
    if not isinstance(value, dict):
        raise ValueError(f"Publication receipt is invalid: {stage_name}.")
    TRAIN.validate_publication_receipt(
        value, preparation, preparation_digest, stage_name
    )
    return value


def validate_receipt_run(
    store: GitHubStore,
    receipt: dict[str, object],
    stage: dict[str, object],
    approved_actor_id: str,
    expected_run_id: str | None = None,
    expected_run_attempt: str | None = None,
) -> None:
    publication = receipt.get("publication")
    if not isinstance(publication, dict):
        raise ValueError("Publication receipt workflow identity is invalid.")
    run_id = str(publication.get("runId", ""))
    run_attempt = str(publication.get("runAttempt", ""))
    if not run_id.isdigit() or not run_attempt.isdigit():
        raise ValueError("Publication receipt workflow identity is invalid.")
    if (
        expected_run_id is not None
        and (run_id != expected_run_id or run_attempt != expected_run_attempt)
    ):
        raise ValueError("Publication receipt does not match the completed workflow run.")
    validate_run(
        store.workflow_run(str(stage["repository"]), run_id),
        stage,
        run_id,
        run_attempt,
        approved_actor_id,
        require_complete=True,
    )


def write_exact(path: Path, contents: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() != contents:
        raise ValueError(f"Output already contains different bytes: {path.name}.")
    path.write_bytes(contents)


def prepare_start(
    store: GitHubStore,
    release_id: int,
    output: Path,
) -> dict[str, object]:
    release = store.release(CORE_REPOSITORY, release_id)
    contents = store.asset_bytes(
        CORE_REPOSITORY, release_id, PREPARATION.PREPARATION_ASSET
    )
    if contents is None:
        raise ValueError("Core preview release has no immutable preparation asset.")
    write_exact(output, contents)
    preparation, digest = TRAIN.read_preparation(output)
    stage = TRAIN.preparation_stage(preparation, CORE_PREVIEW_STAGE)
    if stage.get("releaseId") != release_id:
        raise ValueError("Core preview release ID does not match preparation.")
    validate_published_release(store, release, stage)
    return {
        "status": "PASS",
        "version": preparation["version"],
        "preparationSha256": digest,
        "releaseId": release_id,
    }


def upstream_receipts(
    store: GitHubStore,
    preparation: dict[str, object],
    preparation_digest: str,
    stage: dict[str, object],
    approved_actor_id: str,
    directory: Path,
) -> list[Path]:
    expected = stage.get("upstreamStages")
    if not isinstance(expected, list):
        raise ValueError("Prepared upstream stage list is invalid.")
    result = []
    for upstream_name in expected:
        upstream_stage = TRAIN.preparation_stage(preparation, str(upstream_name))
        release_id = upstream_stage.get("releaseId")
        assert isinstance(release_id, int)
        name = receipt_asset_name(str(upstream_name))
        contents = store.asset_bytes(
            str(upstream_stage["repository"]), release_id, name
        )
        if contents is None:
            raise ValueError(f"Upstream publication receipt is missing: {upstream_name}.")
        receipt = validate_receipt_bytes(
            contents, preparation, preparation_digest, str(upstream_name)
        )
        validate_receipt_run(
            store, receipt, upstream_stage, approved_actor_id
        )
        path = directory / name
        write_exact(path, contents)
        result.append(path)
    return result


def publish_prepared_release(
    store: GitHubStore,
    stage: dict[str, object],
    upstream_paths: list[Path],
    preparation_bytes: bytes,
) -> dict[str, object]:
    repository = str(stage["repository"])
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    release = store.release(repository, release_id)
    if not release_matches_stage(release, stage):
        raise ValueError(f"Prepared release identity changed: {stage['name']}.")
    if release.get("target_commitish") != stage.get("sourceCommit"):
        raise ValueError(f"Prepared release target changed: {stage['name']}.")
    tag_commit = store.tag_commit(repository, str(stage["ref"]))
    if tag_commit is not None and tag_commit != stage.get("sourceCommit"):
        raise ValueError(f"Prepared release tag changed: {stage['name']}.")
    preparation_asset = store.asset_bytes(
        repository, release_id, PREPARATION.PREPARATION_ASSET
    )
    if preparation_asset != preparation_bytes:
        raise ValueError(f"Prepared release approval changed: {stage['name']}.")
    for path in upstream_paths:
        ensure_asset(store, repository, release_id, path.name, path.read_bytes())
    if release.get("draft") is True:
        if stage.get("name") == CORE_PREVIEW_STAGE:
            raise ValueError("Core preview must be published by the owner to start the train.")
        release = store.publish_release(repository, release_id)
    validate_published_release(store, release, stage)
    return release


def expected_run_title(preparation_digest: str, stage_name: str) -> str:
    identity = COORDINATOR.stage_identity(preparation_digest, stage_name)
    return f"Release {stage_name} {identity}"


def recoverable_run(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
) -> dict[str, object] | None:
    matches = []
    title = expected_run_title(preparation_digest, str(stage["name"]))
    runs = store.workflow_runs(
        str(stage["repository"]), str(stage["workflow"]), str(stage["workflowRef"])
    )
    for run in runs:
        if run.get("display_title") != title:
            continue
        run_id = str(run.get("id", ""))
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed" and run.get("conclusion") != "success":
            continue
        matches.append(run)
    if len(matches) > 1:
        raise ValueError(f"Multiple recoverable workflow runs exist: {stage['name']}.")
    return matches[0] if matches else None


def dispatch_coordinates(
    stage: dict[str, object], preparation_digest: str, dispatch: dict[str, object]
) -> bytes:
    value = {
        "schemaVersion": 1,
        "stage": stage["name"],
        "stageIdentity": COORDINATOR.stage_identity(
            preparation_digest, str(stage["name"])
        ),
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "workflowRunId": str(dispatch["workflowRunId"]),
        "dispatchAttemptIdentity": str(dispatch["dispatchAttemptIdentity"]),
    }
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def dispatch_intent(
    stage: dict[str, object], preparation_digest: str, attempt_identity: str
) -> bytes:
    value = {
        "schemaVersion": 1,
        "stage": stage["name"],
        "stageIdentity": COORDINATOR.stage_identity(
            preparation_digest, str(stage["name"])
        ),
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "workflowCommit": stage["workflowCommit"],
        "workflowRef": stage["workflowRef"],
        "dispatchAttemptIdentity": attempt_identity,
    }
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def persisted_recoverable_run(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_digest: str,
    approved_actor_id: str,
) -> tuple[tuple[dict[str, object], str] | None, set[str]]:
    repository = str(stage["repository"])
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    prefix = f"dispatch-coordinate-{stage['name']}-"
    matches = [
        asset for asset in store.assets(repository, release_id)
        if isinstance(asset.get("name"), str)
        and str(asset["name"]).startswith(prefix)
    ]
    candidates: list[tuple[dict[str, object], str]] = []
    coordinate_attempts: set[str] = set()
    for asset in matches:
        name = str(asset["name"])
        suffix = name[len(prefix):]
        if re.fullmatch(r"[1-9][0-9]*\.json", suffix) is None:
            raise ValueError(f"Persisted dispatch coordinate name is invalid: {name}.")
        contents = store.asset_bytes(repository, release_id, name)
        if contents is None:
            raise ValueError(f"Persisted dispatch coordinate is missing: {name}.")
        try:
            value = json.loads(contents)
        except json.JSONDecodeError as error:
            raise ValueError(f"Persisted dispatch coordinate is invalid: {name}.") from error
        expected = {
            "schemaVersion": 1,
            "stage": stage["name"],
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, str(stage["name"])
            ),
            "repository": stage["repository"],
            "workflow": stage["workflow"],
            "workflowRunId": suffix.removesuffix(".json"),
            "dispatchAttemptIdentity": value.get("dispatchAttemptIdentity"),
        }
        attempt_identity = value.get("dispatchAttemptIdentity")
        if (
            not isinstance(attempt_identity, str)
            or COORDINATOR.SHA256.fullmatch(attempt_identity) is None
            or value != expected
            or attempt_identity in coordinate_attempts
        ):
            raise ValueError(f"Persisted dispatch coordinate changed: {name}.")
        coordinate_attempts.add(attempt_identity)
        run_id = str(value["workflowRunId"])
        run = store.workflow_run(repository, run_id)
        run_attempt = str(run.get("run_attempt", ""))
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed" and run.get("conclusion") != "success":
            continue
        candidates.append((run, attempt_identity))
    if len(candidates) > 1:
        raise ValueError(f"Multiple persisted workflow runs are viable: {stage['name']}.")
    return (candidates[0] if candidates else None), coordinate_attempts


def persisted_intents(
    store: GitHubStore,
    stage: dict[str, object],
    preparation_digest: str,
) -> set[str]:
    repository = str(stage["repository"])
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    prefix = f"dispatch-intent-{stage['name']}-"
    identities: set[str] = set()
    for asset in store.assets(repository, release_id):
        name = asset.get("name")
        if not isinstance(name, str) or not name.startswith(prefix):
            continue
        suffix = name[len(prefix):]
        if re.fullmatch(r"[0-9a-f]{64}\.json", suffix) is None:
            raise ValueError(f"Persisted dispatch intent name is invalid: {name}.")
        identity = suffix.removesuffix(".json")
        contents = store.asset_bytes(repository, release_id, name)
        if contents != dispatch_intent(stage, preparation_digest, identity):
            raise ValueError(f"Persisted dispatch intent changed: {name}.")
        if identity in identities:
            raise ValueError(f"Persisted dispatch intent is duplicated: {name}.")
        identities.add(identity)
    return identities


def receipt_from_artifact(
    store: GitHubStore,
    stage: dict[str, object],
    run_id: str,
    run_attempt: str,
) -> bytes:
    expected_name = (
        f"publication-receipt-{stage['name']}-{run_id}-{run_attempt}"
    )
    matches = [
        item for item in store.run_artifacts(str(stage["repository"]), run_id)
        if item.get("name") == expected_name
    ]
    if (
        len(matches) != 1
        or matches[0].get("expired") is not False
        or not isinstance(matches[0].get("id"), int)
    ):
        raise ValueError(f"Publication receipt artifact is missing: {stage['name']}.")
    archive = store.artifact_bytes(
        str(stage["repository"]), int(matches[0]["id"])
    )
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        files = [item for item in bundle.infolist() if not item.is_dir()]
        if len(files) != 1:
            raise ValueError("Publication receipt artifact inventory is invalid.")
        path = PurePosixPath(files[0].filename)
        if (
            path.is_absolute()
            or any(part in {"", ".", ".."} for part in path.parts)
            or path.name != RECEIPT_FILE
            or files[0].file_size > 4 * 1024 * 1024
        ):
            raise ValueError("Publication receipt artifact path is invalid.")
        return bundle.read(files[0])


def wait_for_run(
    store: GitHubStore,
    stage: dict[str, object],
    request: dict[str, object],
    dispatch: dict[str, object],
    approved_actor_id: str,
    *,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
) -> tuple[dict[str, object], str]:
    run_id = str(dispatch["workflowRunId"])
    deadline = clock() + STAGE_TIMEOUT_SECONDS
    while True:
        run = store.workflow_run(str(stage["repository"]), run_id)
        COORDINATOR.validate_workflow_run(
            run, request, dispatch, require_complete=False
        )
        run_attempt = str(run.get("run_attempt", ""))
        if not run_attempt.isdigit():
            raise ValueError("Dispatched workflow run attempt is invalid.")
        validate_run(
            run,
            stage,
            run_id,
            run_attempt,
            approved_actor_id,
            require_complete=False,
        )
        if run.get("status") == "completed":
            validate_run(
                run,
                stage,
                run_id,
                run_attempt,
                approved_actor_id,
                require_complete=True,
            )
            return run, run_attempt
        remaining = deadline - clock()
        if remaining <= 0:
            raise TimeoutError(f"Timed out waiting for release stage: {stage['name']}.")
        sleep(min(POLL_SECONDS, remaining))


def run_stage(
    store: GitHubStore,
    preparation_path: Path,
    stage_name: str,
    approved_actor_id: str,
    output: Path,
) -> dict[str, object]:
    if not approved_actor_id.isdigit() or int(approved_actor_id) < 1:
        raise ValueError("Approved GitHub App actor ID is invalid.")
    preparation, preparation_digest = TRAIN.read_preparation(preparation_path)
    stage = TRAIN.preparation_stage(preparation, stage_name)
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES[:6]}:
        raise ValueError("Only package publication stages are supported here.")
    release_id = stage.get("releaseId")
    assert isinstance(release_id, int)
    repository = str(stage["repository"])
    upstream_directory = output.parent / "upstream"
    upstream_paths = upstream_receipts(
        store,
        preparation,
        preparation_digest,
        stage,
        approved_actor_id,
        upstream_directory,
    )
    preparation_bytes = preparation_path.read_bytes()
    publish_prepared_release(store, stage, upstream_paths, preparation_bytes)

    own_asset = receipt_asset_name(stage_name)
    existing = store.asset_bytes(repository, release_id, own_asset)
    if existing is not None:
        receipt = validate_receipt_bytes(
            existing, preparation, preparation_digest, stage_name
        )
        validate_receipt_run(store, receipt, stage, approved_actor_id)
        write_exact(output, existing)
        return {
            "status": "PASS",
            "stage": stage_name,
            "mode": "resume",
            "receiptSha256": hashlib.sha256(existing).hexdigest(),
        }

    request = COORDINATOR.create_dispatch(
        preparation_path=preparation_path,
        stage_name=stage_name,
        coordinator_repository=COORDINATOR.COORDINATOR_REPOSITORY,
        coordinator_run_id=os.environ.get("GITHUB_RUN_ID", ""),
        coordinator_run_attempt=os.environ.get("GITHUB_RUN_ATTEMPT", ""),
        upstream_receipt_paths={
            str(TRAIN.read_json(path)["stage"]): path for path in upstream_paths
        },
    )
    persisted, coordinate_attempts = persisted_recoverable_run(
        store, stage, preparation_digest, approved_actor_id
    )
    intent_attempts = persisted_intents(store, stage, preparation_digest)
    if not coordinate_attempts.issubset(intent_attempts):
        raise ValueError(f"Persisted dispatch coordinate has no intent: {stage_name}.")
    unresolved_attempts = intent_attempts - coordinate_attempts
    recovered = persisted[0] if persisted is not None else None
    recovered_attempt = persisted[1] if persisted is not None else None
    if recovered is None:
        recovered = recoverable_run(
            store, stage, preparation_digest, approved_actor_id
        )
        if recovered is not None:
            if len(unresolved_attempts) != 1:
                raise ValueError(
                    f"Visible workflow run has no unique dispatch intent: {stage_name}."
                )
            recovered_attempt = next(iter(unresolved_attempts))
    mode = "recovered"
    if recovered is None:
        if unresolved_attempts:
            raise RuntimeError(
                f"Dispatch intent has no visible workflow run yet: {stage_name}."
            )
        request_inputs = request.get("inputs")
        assert isinstance(request_inputs, dict)
        current_attempt = str(request_inputs["dispatch_attempt_identity"])
        intent_name = f"dispatch-intent-{stage_name}-{current_attempt}.json"
        intent_bytes = dispatch_intent(stage, preparation_digest, current_attempt)
        ensure_asset(
            store, repository, release_id, intent_name, intent_bytes
        )
        mode = "dispatch"
        try:
            dispatch = COORDINATOR.dispatch_workflow(
                request, store.token, preparation_path
            )
        except Exception:
            recovered = recoverable_run(
                store, stage, preparation_digest, approved_actor_id
            )
            if recovered is None:
                raise RuntimeError(
                    f"Dispatch response was lost before the workflow run became visible: "
                    f"{stage_name}."
                )
            mode = "recovered"
            recovered_attempt = current_attempt
    if recovered is not None:
        assert recovered_attempt is not None
        run_id = str(recovered["id"])
        dispatch = {
            "workflowRunId": run_id,
            "runUrl": (
                f"https://api.github.com/repos/{repository}/actions/runs/{run_id}"
            ),
            "htmlUrl": f"https://github.com/{repository}/actions/runs/{run_id}",
            "stageIdentity": COORDINATOR.stage_identity(
                preparation_digest, stage_name
            ),
            "dispatchAttemptIdentity": recovered_attempt,
        }
    coordinate_name = (
        f"dispatch-coordinate-{stage_name}-{dispatch['workflowRunId']}.json"
    )
    ensure_asset(
        store,
        repository,
        release_id,
        coordinate_name,
        dispatch_coordinates(stage, preparation_digest, dispatch),
    )
    _, run_attempt = wait_for_run(
        store, stage, request, dispatch, approved_actor_id
    )
    contents = receipt_from_artifact(
        store, stage, str(dispatch["workflowRunId"]), run_attempt
    )
    receipt = validate_receipt_bytes(
        contents, preparation, preparation_digest, stage_name
    )
    validate_receipt_run(
        store,
        receipt,
        stage,
        approved_actor_id,
        str(dispatch["workflowRunId"]),
        run_attempt,
    )
    ensure_asset(store, repository, release_id, own_asset, contents)
    write_exact(output, contents)
    return {
        "status": "PASS",
        "stage": stage_name,
        "mode": mode,
        "workflowRunId": dispatch["workflowRunId"],
        "receiptSha256": hashlib.sha256(contents).hexdigest(),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    start = subparsers.add_parser("prepare-start")
    start.add_argument("--release-id", type=int, required=True)
    start.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    start.add_argument("--output", type=Path, required=True)
    stage = subparsers.add_parser("stage")
    stage.add_argument("--preparation", type=Path, required=True)
    stage.add_argument("--stage", required=True)
    stage.add_argument("--approved-actor-id", required=True)
    stage.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    stage.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    token = os.environ.get(arguments.token_environment, "").strip()
    store = GitHubStore(token)
    if arguments.command == "prepare-start":
        result = prepare_start(store, arguments.release_id, arguments.output)
    else:
        result = run_stage(
            store,
            arguments.preparation,
            arguments.stage,
            arguments.approved_actor_id,
            arguments.output,
        )
    print("NETWASM_RELEASE_STAGE " + json.dumps(result, sort_keys=True), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
