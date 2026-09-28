#!/usr/bin/env python3
"""Create immutable release dispatches and verify their exact workflow runs."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
from typing import Callable
import urllib.parse
import urllib.request


SCRIPT_ROOT = Path(__file__).resolve().parent
TRAIN_SCRIPT = SCRIPT_ROOT / "release-train.py"
TRAIN_SPEC = importlib.util.spec_from_file_location("release_train", TRAIN_SCRIPT)
assert TRAIN_SPEC is not None and TRAIN_SPEC.loader is not None
TRAIN = importlib.util.module_from_spec(TRAIN_SPEC)
TRAIN_SPEC.loader.exec_module(TRAIN)

API_VERSION = "2022-11-28"
COORDINATOR_REPOSITORY = "zion-sati/NetWasm"
SHA256 = re.compile(r"[0-9a-f]{64}")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]*")


def stage_identity(preparation_sha256: str, stage_name: str) -> str:
    if SHA256.fullmatch(preparation_sha256) is None:
        raise ValueError("Coordinator preparation digest is invalid.")
    if stage_name not in {item[0] for item in TRAIN.PREPARATION_STAGES}:
        raise ValueError("Coordinator stage is unknown.")
    return hashlib.sha256(
        f"{preparation_sha256}:{stage_name}".encode("utf-8")
    ).hexdigest()


def dispatch_attempt_identity(
    preparation_sha256: str,
    stage_name: str,
    coordinator_run_id: str,
    coordinator_run_attempt: str,
) -> str:
    if (
        POSITIVE_INTEGER.fullmatch(coordinator_run_id) is None
        or POSITIVE_INTEGER.fullmatch(coordinator_run_attempt) is None
    ):
        raise ValueError("Coordinator workflow coordinates are invalid.")
    stable = stage_identity(preparation_sha256, stage_name)
    return hashlib.sha256(
        f"{stable}:{coordinator_run_id}:{coordinator_run_attempt}".encode("utf-8")
    ).hexdigest()


def upstream_coordinates(
    preparation: dict[str, object],
    stage_name: str,
    receipt_paths: dict[str, Path],
) -> list[dict[str, str]]:
    stage = TRAIN.preparation_stage(preparation, stage_name)
    expected = stage.get("upstreamStages")
    if not isinstance(expected, list):
        raise ValueError("Prepared upstream stage list is invalid.")
    if set(receipt_paths) != set(expected):
        raise ValueError("Coordinator upstream receipt set is incomplete or unexpected.")
    result = []
    for name in expected:
        path = receipt_paths[name]
        if not path.is_file():
            raise ValueError(f"Coordinator upstream receipt is missing: {name}.")
        result.append({
            "stage": name,
            "sha256": TRAIN.sha256(path),
            "fileName": path.name,
        })
    return result


def create_dispatch(
    *,
    preparation_path: Path,
    stage_name: str,
    coordinator_repository: str,
    coordinator_run_id: str,
    coordinator_run_attempt: str,
    upstream_receipt_paths: dict[str, Path],
) -> dict[str, object]:
    preparation, digest = TRAIN.read_preparation(preparation_path)
    stage = TRAIN.preparation_stage(preparation, stage_name)
    if not re.fullmatch(
        r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", coordinator_repository
    ):
        raise ValueError("Coordinator repository is invalid.")
    upstream = upstream_coordinates(
        preparation, stage_name, upstream_receipt_paths
    )
    stable_identity = stage_identity(digest, stage_name)
    attempt_identity = dispatch_attempt_identity(
        digest, stage_name, coordinator_run_id, coordinator_run_attempt
    )
    return {
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "ref": stage["workflowRef"],
        "expectedWorkflowSha": stage["workflowCommit"],
        "inputs": {
            "coordinated_stage": stage_name,
            "preparation_sha256": digest,
            "release_id": str(stage["releaseId"] or ""),
            "release_ref": str(stage["ref"]),
            "source_commit": str(stage["sourceCommit"]),
            "infrastructure_commit": str(stage["infrastructureCommit"]),
            "coordinator_repository": coordinator_repository,
            "coordinator_run_id": coordinator_run_id,
            "coordinator_run_attempt": coordinator_run_attempt,
            "stage_identity": stable_identity,
            "dispatch_attempt_identity": attempt_identity,
            "upstream_receipts": json.dumps(
                upstream, ensure_ascii=True, separators=(",", ":"), sort_keys=True
            ),
        },
        "return_run_details": True,
    }


def dispatch_url(request: dict[str, object]) -> str:
    repository = request.get("repository")
    workflow = request.get("workflow")
    if not isinstance(repository, str) or not isinstance(workflow, str):
        raise ValueError("Dispatch request target is invalid.")
    workflow_name = Path(workflow).name
    if workflow != f".github/workflows/{workflow_name}":
        raise ValueError("Dispatch workflow path is invalid.")
    return (
        f"https://api.github.com/repos/{repository}/actions/workflows/"
        f"{urllib.parse.quote(workflow_name, safe='')}/dispatches"
    )


def validate_dispatch_request(
    request: dict[str, object], preparation_path: Path
) -> None:
    if set(request) != {
        "repository", "workflow", "ref", "expectedWorkflowSha", "inputs",
        "return_run_details",
    } or request.get("return_run_details") is not True:
        raise ValueError("Dispatch request fields are invalid.")
    preparation, digest = TRAIN.read_preparation(preparation_path)
    inputs = request.get("inputs")
    input_fields = {
        "coordinated_stage", "preparation_sha256", "release_id", "release_ref",
        "source_commit", "infrastructure_commit", "coordinator_repository",
        "coordinator_run_id", "coordinator_run_attempt", "stage_identity",
        "dispatch_attempt_identity", "upstream_receipts",
    }
    if not isinstance(inputs, dict) or set(inputs) != input_fields:
        raise ValueError("Dispatch request inputs are invalid.")
    stage_name = inputs.get("coordinated_stage")
    if not isinstance(stage_name, str):
        raise ValueError("Dispatch request stage is invalid.")
    stage = TRAIN.preparation_stage(preparation, stage_name)
    expected = {
        "repository": stage["repository"],
        "workflow": stage["workflow"],
        "ref": stage["workflowRef"],
        "expectedWorkflowSha": stage["workflowCommit"],
    }
    if any(request.get(name) != value for name, value in expected.items()):
        raise ValueError("Dispatch request target does not match preparation.")
    if inputs.get("coordinator_repository") != COORDINATOR_REPOSITORY:
        raise ValueError("Dispatch coordinator repository is not approved.")
    expected_inputs = {
        "preparation_sha256": digest,
        "release_id": str(stage["releaseId"] or ""),
        "release_ref": str(stage["ref"]),
        "source_commit": str(stage["sourceCommit"]),
        "infrastructure_commit": str(stage["infrastructureCommit"]),
        "stage_identity": stage_identity(digest, stage_name),
    }
    if any(inputs.get(name) != value for name, value in expected_inputs.items()):
        raise ValueError("Dispatch request identity does not match preparation.")
    run_id = inputs.get("coordinator_run_id")
    run_attempt = inputs.get("coordinator_run_attempt")
    if not isinstance(run_id, str) or not isinstance(run_attempt, str):
        raise ValueError("Dispatch coordinator workflow coordinates are invalid.")
    if inputs.get("dispatch_attempt_identity") != dispatch_attempt_identity(
        digest, stage_name, run_id, run_attempt
    ):
        raise ValueError("Dispatch attempt identity is invalid.")
    try:
        upstream = json.loads(str(inputs.get("upstream_receipts")))
    except json.JSONDecodeError as error:
        raise ValueError("Dispatch upstream receipts are invalid.") from error
    expected_upstream = stage.get("upstreamStages")
    if not isinstance(upstream, list) or not isinstance(expected_upstream, list):
        raise ValueError("Dispatch upstream receipts are invalid.")
    if [item.get("stage") for item in upstream if isinstance(item, dict)] != expected_upstream:
        raise ValueError("Dispatch upstream receipt stages do not match preparation.")
    for item in upstream:
        if not isinstance(item, dict) or set(item) != {"stage", "sha256", "fileName"}:
            raise ValueError("Dispatch upstream receipt coordinates are invalid.")
        file_name = item.get("fileName")
        if (
            not isinstance(item.get("sha256"), str)
            or SHA256.fullmatch(str(item["sha256"])) is None
            or not isinstance(file_name, str)
            or not file_name
            or Path(file_name).name != file_name
        ):
            raise ValueError("Dispatch upstream receipt coordinates are invalid.")


def dispatch_workflow(
    request: dict[str, object],
    token: str,
    preparation_path: Path,
    *,
    open_request: Callable[..., object] = urllib.request.urlopen,
) -> dict[str, object]:
    validate_dispatch_request(request, preparation_path)
    if not token:
        raise ValueError("GitHub App installation token is required.")
    body = {
        "ref": request["ref"],
        "inputs": request["inputs"],
        "return_run_details": True,
    }
    api_request = urllib.request.Request(
        dispatch_url(request),
        data=json.dumps(body, separators=(",", ":")).encode("utf-8"),
        method="POST",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": API_VERSION,
            "Content-Type": "application/json",
            "User-Agent": "NetWasm-release-coordinator",
        },
    )
    with open_request(api_request, timeout=30) as response:
        if getattr(response, "status", None) != 200:
            raise ValueError("GitHub did not return workflow-run details for dispatch.")
        value = json.load(response)
    if not isinstance(value, dict):
        raise ValueError("GitHub workflow dispatch response is invalid.")
    run_id = value.get("workflow_run_id")
    run_url = value.get("run_url")
    html_url = value.get("html_url")
    if (
        not isinstance(run_id, int)
        or isinstance(run_id, bool)
        or run_id < 1
        or not isinstance(run_url, str)
        or not run_url.startswith("https://api.github.com/repos/")
        or not isinstance(html_url, str)
        or not html_url.startswith("https://github.com/")
    ):
        raise ValueError("GitHub workflow dispatch did not return exact run coordinates.")
    inputs = request.get("inputs")
    repository = request["repository"]
    expected_run_url = (
        f"https://api.github.com/repos/{repository}/actions/runs/{run_id}"
    )
    expected_html_url = f"https://github.com/{repository}/actions/runs/{run_id}"
    if run_url != expected_run_url or html_url != expected_html_url:
        raise ValueError("GitHub workflow dispatch response target is invalid.")
    inputs = request["inputs"]
    assert isinstance(inputs, dict)
    return {
        "workflowRunId": str(run_id),
        "runUrl": run_url,
        "htmlUrl": html_url,
        "stageIdentity": inputs["stage_identity"],
        "dispatchAttemptIdentity": inputs["dispatch_attempt_identity"],
    }


def validate_workflow_run(
    run: dict[str, object],
    request: dict[str, object],
    dispatch: dict[str, object],
    *,
    require_complete: bool,
) -> None:
    expected_run_id = int(str(dispatch.get("workflowRunId")))
    path = run.get("path")
    workflow = request.get("workflow")
    if (
        run.get("id") != expected_run_id
        or run.get("event") != "workflow_dispatch"
        or run.get("head_sha") != request.get("expectedWorkflowSha")
        or run.get("head_branch") != request.get("ref")
        or path != workflow
    ):
        raise ValueError("Dispatched workflow run identity does not match the stage.")
    if require_complete and (
        run.get("status") != "completed" or run.get("conclusion") != "success"
    ):
        raise ValueError("Dispatched workflow run has not completed successfully.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    create = subparsers.add_parser("create-dispatch")
    create.add_argument("--preparation", type=Path, required=True)
    create.add_argument("--stage", required=True)
    create.add_argument("--coordinator-repository", required=True)
    create.add_argument("--coordinator-run-id", required=True)
    create.add_argument("--coordinator-run-attempt", required=True)
    create.add_argument("--upstream-receipt", type=Path, action="append", default=[])
    create.add_argument("--output", type=Path, required=True)
    send = subparsers.add_parser("dispatch")
    send.add_argument("--preparation", type=Path, required=True)
    send.add_argument("--stage", required=True)
    send.add_argument("--coordinator-repository", required=True)
    send.add_argument("--coordinator-run-id", required=True)
    send.add_argument("--coordinator-run-attempt", required=True)
    send.add_argument("--upstream-receipt", type=Path, action="append", default=[])
    send.add_argument("--token-environment", default="GITHUB_APP_TOKEN")
    send.add_argument("--request-output", type=Path)
    send.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify-run")
    verify.add_argument("--request", type=Path, required=True)
    verify.add_argument("--dispatch", type=Path, required=True)
    verify.add_argument("--run", type=Path, required=True)
    verify.add_argument("--require-complete", action="store_true")
    verify.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    if arguments.command == "create-dispatch":
        receipts: dict[str, Path] = {}
        for path in arguments.upstream_receipt:
            value = TRAIN.read_json(path)
            stage = value.get("stage")
            if not isinstance(stage, str) or stage in receipts:
                raise ValueError("Upstream receipt stage is missing or duplicated.")
            receipts[stage] = path
        value = create_dispatch(
            preparation_path=arguments.preparation,
            stage_name=arguments.stage,
            coordinator_repository=arguments.coordinator_repository,
            coordinator_run_id=arguments.coordinator_run_id,
            coordinator_run_attempt=arguments.coordinator_run_attempt,
            upstream_receipt_paths=receipts,
        )
    elif arguments.command == "dispatch":
        receipts = {}
        for path in arguments.upstream_receipt:
            receipt = TRAIN.read_json(path)
            stage = receipt.get("stage")
            if not isinstance(stage, str) or stage in receipts:
                raise ValueError("Upstream receipt stage is missing or duplicated.")
            receipts[stage] = path
        request = create_dispatch(
            preparation_path=arguments.preparation,
            stage_name=arguments.stage,
            coordinator_repository=arguments.coordinator_repository,
            coordinator_run_id=arguments.coordinator_run_id,
            coordinator_run_attempt=arguments.coordinator_run_attempt,
            upstream_receipt_paths=receipts,
        )
        token = os.environ.get(arguments.token_environment, "").strip()
        if arguments.request_output is not None:
            arguments.request_output.parent.mkdir(parents=True, exist_ok=True)
            arguments.request_output.write_text(
                json.dumps(request, indent=2) + "\n", encoding="utf-8"
            )
        value = dispatch_workflow(request, token, arguments.preparation)
    else:
        request = TRAIN.read_json(arguments.request)
        dispatch = TRAIN.read_json(arguments.dispatch)
        run = TRAIN.read_json(arguments.run)
        validate_workflow_run(
            run, request, dispatch, require_complete=arguments.require_complete
        )
        value = {"status": "PASS", "workflowRunId": dispatch["workflowRunId"]}
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
