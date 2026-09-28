#!/usr/bin/env python3
"""Create and resolve exact-tree NetWasm CI qualification receipts."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from typing import Callable
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener
import zipfile
from io import BytesIO


SCHEMA_VERSION = 1
ARTIFACT_NAME = "ci-qualification"
RECEIPT_NAME = "ci-qualification.json"
MAX_RECEIPT_BYTES = 64 * 1024


def git(root: Path, *arguments: str) -> str:
    return subprocess.check_output(
        ["git", "-C", str(root), *arguments],
        text=True,
    ).strip()


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def create_receipt(
    root: Path,
    repository: str,
    event: dict[str, object],
    run_id: str,
    run_attempt: str,
    workflow: Path,
) -> dict[str, object]:
    pull_request = event.get("pull_request")
    if not isinstance(pull_request, dict):
        raise ValueError("qualification receipts can only be created by pull-request runs")
    head = pull_request.get("head")
    base = pull_request.get("base")
    if not isinstance(head, dict) or not isinstance(base, dict):
        raise ValueError("pull-request event is missing head/base identities")

    commit = git(root, "rev-parse", "HEAD")
    return {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "eventName": "pull_request",
        "pullRequest": pull_request.get("number"),
        "pullRequestHead": head.get("sha"),
        "pullRequestBase": base.get("sha"),
        "checkedOutCommit": commit,
        "checkedOutTree": git(root, "rev-parse", f"{commit}^{{tree}}"),
        "workflowSha256": sha256(root / workflow),
        "workflowRunId": str(run_id),
        "workflowRunAttempt": str(run_attempt),
    }


def validate_receipt(
    receipt: dict[str, object],
    *,
    repository: str,
    pull_request_head: str,
    tree: str,
    workflow_sha256: str,
    run_id: int,
) -> None:
    expected = {
        "schemaVersion": SCHEMA_VERSION,
        "repository": repository,
        "eventName": "pull_request",
        "pullRequestHead": pull_request_head,
        "checkedOutTree": tree,
        "workflowSha256": workflow_sha256,
        "workflowRunId": str(run_id),
    }
    mismatches = {
        name: (receipt.get(name), value)
        for name, value in expected.items()
        if receipt.get(name) != value
    }
    if mismatches:
        raise ValueError(f"qualification receipt does not match the merge: {mismatches}")


class SafeAuthorizationRedirectHandler(HTTPRedirectHandler):
    """Keep GitHub credentials off cross-origin artifact redirects."""

    def redirect_request(self, request, fp, code, message, headers, new_url):
        redirected = super().redirect_request(
            request, fp, code, message, headers, new_url,
        )
        if (
            redirected is not None
            and urlsplit(request.full_url).netloc != urlsplit(new_url).netloc
        ):
            redirected.remove_header("Authorization")
        return redirected


class GitHubApi:
    def __init__(self, token: str) -> None:
        if not token:
            raise ValueError("GITHUB_TOKEN is required to resolve CI qualification")
        self.headers = {
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "NetWasm-CI",
        }
        self.opener = build_opener(SafeAuthorizationRedirectHandler())

    def bytes(self, url: str) -> bytes:
        with self.opener.open(Request(url, headers=self.headers), timeout=30) as response:
            return response.read()

    def json(self, url: str) -> dict[str, object]:
        return json.loads(self.bytes(url))


def receipt_from_archive(archive: bytes) -> dict[str, object]:
    with zipfile.ZipFile(BytesIO(archive)) as bundle:
        names = bundle.namelist()
        if names != [RECEIPT_NAME]:
            raise ValueError(f"qualification artifact has unexpected contents: {names}")
        if bundle.getinfo(RECEIPT_NAME).file_size > MAX_RECEIPT_BYTES:
            raise ValueError("qualification receipt exceeds the size limit")
        data = bundle.read(RECEIPT_NAME)
    value = json.loads(data)
    if not isinstance(value, dict):
        raise ValueError("qualification receipt is not a JSON object")
    return value


def find_qualification(
    root: Path,
    repository: str,
    workflow: Path,
    api_json: Callable[[str], dict[str, object]],
    api_bytes: Callable[[str], bytes],
) -> int | None:
    commit = git(root, "rev-parse", "HEAD")
    parents = git(root, "rev-list", "--parents", "-n", "1", commit).split()[1:]
    if len(parents) != 2:
        return None
    pull_request_head = parents[1]
    tree = git(root, "rev-parse", f"{commit}^{{tree}}")
    if tree != git(root, "rev-parse", f"{pull_request_head}^{{tree}}"):
        return None

    query = urlencode({
        "event": "pull_request",
        "status": "success",
        "head_sha": pull_request_head,
        "per_page": 20,
    })
    runs_url = f"https://api.github.com/repos/{repository}/actions/workflows/{workflow.name}/runs?{query}"
    runs = api_json(runs_url).get("workflow_runs", [])
    if not isinstance(runs, list):
        raise ValueError("GitHub workflow-runs response is malformed")
    workflow_digest = sha256(root / workflow)
    for run in runs:
        if not isinstance(run, dict) or run.get("head_sha") != pull_request_head:
            continue
        run_id = run.get("id")
        if not isinstance(run_id, int):
            continue
        artifacts_url = f"https://api.github.com/repos/{repository}/actions/runs/{run_id}/artifacts"
        artifacts = api_json(artifacts_url).get("artifacts", [])
        if not isinstance(artifacts, list):
            continue
        for artifact in artifacts:
            if not isinstance(artifact, dict) or artifact.get("name") != ARTIFACT_NAME:
                continue
            if artifact.get("expired") is True:
                continue
            download = artifact.get("archive_download_url")
            if not isinstance(download, str):
                continue
            receipt = receipt_from_archive(api_bytes(download))
            validate_receipt(
                receipt,
                repository=repository,
                pull_request_head=pull_request_head,
                tree=tree,
                workflow_sha256=workflow_digest,
                run_id=run_id,
            )
            return run_id
    return None


def main() -> int:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)
    create = subparsers.add_parser("create")
    create.add_argument("--root", type=Path, default=Path.cwd())
    create.add_argument("--repository", required=True)
    create.add_argument("--event", type=Path, required=True)
    create.add_argument("--run-id", required=True)
    create.add_argument("--run-attempt", required=True)
    create.add_argument("--workflow", type=Path, required=True)
    create.add_argument("--output", type=Path, required=True)
    find = subparsers.add_parser("find")
    find.add_argument("--root", type=Path, default=Path.cwd())
    find.add_argument("--repository", required=True)
    find.add_argument("--workflow", type=Path, required=True)
    args = parser.parse_args()

    try:
        if args.command == "create":
            event = json.loads(args.event.read_text(encoding="utf-8"))
            receipt = create_receipt(
                args.root.resolve(), args.repository, event,
                args.run_id, args.run_attempt, args.workflow,
            )
            args.output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
            return 0
        api = GitHubApi(os.environ.get("GITHUB_TOKEN", ""))
        run_id = find_qualification(
            args.root.resolve(), args.repository, args.workflow,
            api.json, api.bytes,
        )
        if run_id is None:
            return 1
        print(run_id)
        return 0
    except (OSError, ValueError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        print(f"CI qualification unavailable: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
