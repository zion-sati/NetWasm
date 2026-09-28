#!/usr/bin/env python3
"""Require successful public-main CI for an exact release source commit."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
from typing import Callable
from urllib.parse import quote, urlencode
from urllib.request import Request, urlopen


def find_successful_run(
    repository: str,
    workflow: str,
    source_commit: str,
    default_branch: str,
    fetch: Callable[[str], dict[str, object]],
) -> int:
    if re.fullmatch(r"[0-9a-f]{40}", source_commit) is None:
        raise ValueError("Release CI source commit must be a full lowercase Git object ID.")
    query = urlencode({
        "branch": default_branch,
        "event": "push",
        "status": "success",
        "head_sha": source_commit,
        "per_page": 20,
    })
    workflow_id = quote(Path(workflow).name, safe="")
    url = (
        f"https://api.github.com/repos/{repository}/actions/workflows/"
        f"{workflow_id}/runs?{query}"
    )
    runs = fetch(url).get("workflow_runs")
    if not isinstance(runs, list):
        raise ValueError("GitHub workflow-runs response is malformed.")
    matches = [
        run for run in runs
        if isinstance(run, dict)
        and run.get("head_sha") == source_commit
        and run.get("head_branch") == default_branch
        and run.get("event") == "push"
        and run.get("status") == "completed"
        and run.get("conclusion") == "success"
        and isinstance(run.get("id"), int)
    ]
    if not matches:
        raise ValueError(
            f"No successful {workflow} push run qualifies source commit {source_commit}."
        )
    return int(matches[0]["id"])


class GitHubApi:
    def __init__(self, token: str) -> None:
        if not token:
            raise ValueError("GITHUB_TOKEN is required to verify release CI.")
        self.headers = {
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "NetWasm-Release",
        }

    def json(self, url: str) -> dict[str, object]:
        with urlopen(Request(url, headers=self.headers), timeout=30) as response:
            value = json.load(response)
        if not isinstance(value, dict):
            raise ValueError("GitHub API response is not an object.")
        return value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--workflow", default=".github/workflows/ci.yml")
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--default-branch", required=True)
    parser.add_argument("--github-output", type=Path)
    arguments = parser.parse_args()
    api = GitHubApi(os.environ.get("GITHUB_TOKEN", ""))
    run_id = find_successful_run(
        arguments.repository,
        arguments.workflow,
        arguments.source_commit,
        arguments.default_branch,
        api.json,
    )
    if arguments.github_output is not None:
        with arguments.github_output.open("a", encoding="utf-8") as stream:
            stream.write(f"ci_run_id={run_id}\n")
    print(f"Release source is qualified by CI run {run_id}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
