#!/usr/bin/env python3
"""Create and verify immutable preview/stable release-train bundles."""

from __future__ import annotations

import argparse
import hashlib
from io import BufferedReader
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import zipfile


SCHEMA_VERSION = 1
MAX_ENTRY_BYTES = 512 * 1024 * 1024
MAX_BUNDLE_CONTENT_BYTES = 2 * 1024 * 1024 * 1024
COMMIT = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
VERSION = re.compile(
    r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
)
WORKFLOW_REF = re.compile(r"[A-Za-z0-9][A-Za-z0-9._/-]{0,199}")
SAFE_LEAF = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,199}")

PREPARATION_SCHEMA_VERSION = 1
PUBLICATION_RECEIPT_SCHEMA_VERSION = 1
DELIVERY_RECEIPT_SCHEMA_VERSION = 1
PREPARATION_STAGES = (
    (
        "core-preview", "zion-sati/NetWasm", ".github/workflows/release.yml",
        (), "core-preview",
    ),
    (
        "core-stable", "zion-sati/NetWasm", ".github/workflows/release.yml",
        ("core-preview",), "core-stable",
    ),
    (
        "tunit-preview", "zion-sati/TUnit-NetWasm",
        ".github/workflows/netwasm-release.yml", ("core-stable",),
        "tunit-preview",
    ),
    (
        "tunit-stable", "zion-sati/TUnit-NetWasm",
        ".github/workflows/netwasm-release.yml", ("tunit-preview",),
        "tunit-stable",
    ),
    (
        "libraries-preview", "zion-sati/NetWasm.Libraries",
        ".github/workflows/release.yml", ("core-stable", "tunit-stable"),
        "libraries-preview",
    ),
    (
        "libraries-stable", "zion-sati/NetWasm.Libraries",
        ".github/workflows/release.yml", ("libraries-preview",),
        "libraries-stable",
    ),
    (
        "playground", "zion-sati/NetWasm.Playground",
        ".github/workflows/release.yml",
        ("core-stable", "tunit-stable", "libraries-stable"), "playground",
    ),
    (
        "website", "zion-sati/netwasm.com", ".github/workflows/pages.yml",
        ("playground",), "website",
    ),
)


def sha256_stream(stream: BufferedReader) -> str:
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
    return digest.hexdigest()


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return sha256_stream(stream)


def read_json(path: Path) -> dict[str, object]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON document is not an object: {path}")
    return value


def expected_stage_ref(name: str, version: str) -> str | None:
    preview = f"{version}-preview.1"
    return {
        "core-preview": f"v{preview}",
        "core-stable": f"v{version}",
        "tunit-preview": f"netwasm-v{preview}",
        "tunit-stable": f"netwasm-v{version}",
        "libraries-preview": f"v{preview}",
        "libraries-stable": f"v{version}",
        "playground": f"v{version}",
        "website": None,
    }[name]


def validate_preparation(preparation: dict[str, object]) -> dict[str, object]:
    required = {"schemaVersion", "version", "stages", "policy"}
    if set(preparation) != required:
        raise ValueError("Release preparation root fields do not match schema version 1.")
    if preparation.get("schemaVersion") != PREPARATION_SCHEMA_VERSION:
        raise ValueError("Release preparation schema version is unsupported.")
    version = preparation.get("version")
    if not isinstance(version, str) or VERSION.fullmatch(version) is None:
        raise ValueError("Release preparation version is invalid.")
    policy = preparation.get("policy")
    if not isinstance(policy, dict) or policy != {
        "publicationReceiptSchemaVersion": PUBLICATION_RECEIPT_SCHEMA_VERSION,
        "completionStage": "website",
    }:
        raise ValueError("Release preparation policy is invalid.")
    stages = preparation.get("stages")
    if not isinstance(stages, list) or len(stages) != len(PREPARATION_STAGES):
        raise ValueError("Release preparation must contain the complete ordered stage list.")
    stage_fields = {
        "name", "repository", "workflow", "sourceCommit",
        "infrastructureCommit", "workflowCommit", "workflowRef", "ref",
        "releaseId", "prerelease",
        "upstreamStages",
    }
    identity_by_repository: dict[str, tuple[str, str, str]] = {}
    for actual, expected in zip(stages, PREPARATION_STAGES, strict=True):
        if not isinstance(actual, dict) or set(actual) != stage_fields:
            raise ValueError("Release preparation stage fields are invalid.")
        name, repository, workflow, upstream, _ = expected
        if (
            actual.get("name") != name
            or actual.get("repository") != repository
            or actual.get("workflow") != workflow
            or actual.get("upstreamStages") != list(upstream)
        ):
            raise ValueError(f"Release preparation stage identity is invalid: {name}.")
        for field in ("sourceCommit", "infrastructureCommit", "workflowCommit"):
            value = actual.get(field)
            if not isinstance(value, str) or COMMIT.fullmatch(value) is None:
                raise ValueError(f"Release preparation {name} {field} is invalid.")
        workflow_ref = actual.get("workflowRef")
        if (
            not isinstance(workflow_ref, str)
            or WORKFLOW_REF.fullmatch(workflow_ref) is None
            or ".." in workflow_ref
            or "//" in workflow_ref
            or "@{" in workflow_ref
            or workflow_ref.endswith((".", "/", ".lock"))
        ):
            raise ValueError(f"Release preparation {name} workflowRef is invalid.")
        identity = (
            str(actual["sourceCommit"]),
            str(actual["infrastructureCommit"]),
            str(actual["workflowCommit"]),
        )
        previous = identity_by_repository.setdefault(repository, identity)
        if previous != identity:
            raise ValueError(
                "Release preparation stages use different source or infrastructure "
                f"commits: {repository}."
            )
        source_commit = identity[0]
        expected_ref = expected_stage_ref(name, version)
        if name == "website":
            if (
                actual.get("ref") != source_commit
                or actual.get("releaseId") is not None
                or actual.get("prerelease") is not False
            ):
                raise ValueError("Website preparation coordinates are invalid.")
            continue
        release_id = actual.get("releaseId")
        if (
            actual.get("ref") != expected_ref
            or not isinstance(release_id, int)
            or isinstance(release_id, bool)
            or release_id <= 0
        ):
            raise ValueError(f"Release preparation release coordinates are invalid: {name}.")
        expected_prerelease = name.endswith("-preview")
        if actual.get("prerelease") is not expected_prerelease:
            raise ValueError(f"Release preparation prerelease policy is invalid: {name}.")
    return preparation


def read_preparation(path: Path) -> tuple[dict[str, object], str]:
    contents = path.read_bytes()
    try:
        value = json.loads(contents)
    except json.JSONDecodeError as error:
        raise ValueError(f"Invalid release preparation JSON: {path}") from error
    if not isinstance(value, dict):
        raise ValueError("Release preparation must be a JSON object.")
    return validate_preparation(value), hashlib.sha256(contents).hexdigest()


def preparation_stage(
    preparation: dict[str, object], name: str
) -> dict[str, object]:
    validate_preparation(preparation)
    stages = preparation["stages"]
    assert isinstance(stages, list)
    matches = [stage for stage in stages if stage.get("name") == name]
    if len(matches) != 1:
        raise ValueError(f"Release preparation does not contain one stage named {name}.")
    return matches[0]


def stage_state_anchor(
    preparation: dict[str, object], name: str
) -> tuple[str, int]:
    """Resolve the release that durably owns one stage's coordinator state."""
    stage = preparation_stage(preparation, name)
    release_id = stage.get("releaseId")
    if isinstance(release_id, int) and not isinstance(release_id, bool):
        return str(stage["repository"]), release_id
    if name != "website":
        raise ValueError(f"Release stage has no durable state anchor: {name}.")
    core_preview = preparation_stage(preparation, "core-preview")
    core_release_id = core_preview.get("releaseId")
    if not isinstance(core_release_id, int) or isinstance(core_release_id, bool):
        raise ValueError("Core preview release cannot anchor website state.")
    return str(core_preview["repository"]), core_release_id


DELIVERY_KINDS = {
    "playground-toolchain-candidate": "playground",
    "playground-site-candidate": "playground",
    "playground-completion": "playground",
    "website-site-candidate": "website",
    "website-completion": "website",
}


def release_stage_identity(preparation_sha256: str, stage_name: str) -> str:
    if SHA256.fullmatch(preparation_sha256) is None:
        raise ValueError("Delivery preparation digest is invalid.")
    if stage_name not in {item[0] for item in PREPARATION_STAGES}:
        raise ValueError("Delivery stage is unknown.")
    return hashlib.sha256(
        f"{preparation_sha256}:{stage_name}".encode("utf-8")
    ).hexdigest()


def require_positive_integer(value: object, description: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < 1:
        raise ValueError(f"{description} must be a positive integer.")
    return value


def require_safe_leaf(value: object, description: str) -> str:
    if (
        not isinstance(value, str)
        or SAFE_LEAF.fullmatch(value) is None
        or value in {".", ".."}
        or value.endswith(".")
    ):
        raise ValueError(f"{description} is not a safe leaf name.")
    return value


def validate_archive_descriptor(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "fileName", "bytes", "sha256",
    }:
        raise ValueError("Delivery archive descriptor fields are invalid.")
    file_name = value.get("fileName")
    size = value.get("bytes")
    digest = value.get("sha256")
    if (
        not isinstance(size, int)
        or isinstance(size, bool)
        or size < 1
        or size > MAX_BUNDLE_CONTENT_BYTES
        or not isinstance(digest, str)
        or SHA256.fullmatch(digest) is None
    ):
        raise ValueError("Delivery archive descriptor is invalid.")
    require_safe_leaf(file_name, "Delivery archive file name")
    return value


def validate_artifact_descriptor(
    value: object,
    *,
    repository: str,
    producer: dict[str, object],
) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "repository", "runId", "runAttempt", "artifactId", "artifactName",
    }:
        raise ValueError("Delivery artifact descriptor fields are invalid.")
    artifact_name = value.get("artifactName")
    require_positive_integer(value.get("runId"), "Delivery artifact run ID")
    require_positive_integer(
        value.get("runAttempt"), "Delivery artifact run attempt"
    )
    if (
        value.get("repository") != repository
        or value.get("runId") != producer.get("runId")
        or value.get("runAttempt") != producer.get("runAttempt")
    ):
        raise ValueError("Delivery artifact descriptor identity is invalid.")
    require_positive_integer(value.get("artifactId"), "Delivery artifact ID")
    require_safe_leaf(artifact_name, "Delivery artifact name")
    return value


def delivery_candidate_payload_artifact_name(
    kind: str, run_id: int, run_attempt: int
) -> str:
    if kind not in {
        "playground-toolchain-candidate",
        "playground-site-candidate",
        "website-site-candidate",
    }:
        raise ValueError("Delivery candidate kind is invalid.")
    require_positive_integer(run_id, "Delivery candidate run ID")
    require_positive_integer(run_attempt, "Delivery candidate run attempt")
    return f"{kind.removesuffix('-candidate')}-payload-{run_id}-{run_attempt}"


def validate_delivery_envelope(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_sha256: str,
    expected_kind: str,
) -> tuple[dict[str, object], dict[str, object]]:
    validate_preparation(preparation)
    expected_stage = DELIVERY_KINDS.get(expected_kind)
    if expected_stage is None or receipt.get("kind") != expected_kind:
        raise ValueError("Delivery receipt kind is invalid.")
    stage = preparation_stage(preparation, expected_stage)
    anchor_repository, anchor_release_id = stage_state_anchor(
        preparation, expected_stage
    )
    if (
        not isinstance(receipt.get("schemaVersion"), int)
        or isinstance(receipt.get("schemaVersion"), bool)
        or receipt.get("schemaVersion") != DELIVERY_RECEIPT_SCHEMA_VERSION
    ):
        raise ValueError("Delivery receipt schema version is invalid.")
    expected = {
        "stage": expected_stage,
        "preparationSha256": preparation_sha256,
        "stageIdentity": release_stage_identity(
            preparation_sha256, expected_stage
        ),
        "repository": stage["repository"],
        "sourceCommit": stage["sourceCommit"],
        "infrastructureCommit": stage["infrastructureCommit"],
        "workflowCommit": stage["workflowCommit"],
        "workflowRef": stage["workflowRef"],
    }
    if any(receipt.get(name) != value for name, value in expected.items()):
        raise ValueError("Delivery receipt does not match its prepared stage.")
    anchor = receipt.get("stateAnchor")
    if not isinstance(anchor, dict) or anchor != {
        "repository": anchor_repository,
        "releaseId": anchor_release_id,
    }:
        raise ValueError("Delivery receipt state anchor is invalid.")
    require_positive_integer(
        anchor.get("releaseId") if isinstance(anchor, dict) else None,
        "Delivery state-anchor release ID",
    )
    producer = receipt.get("producer")
    if not isinstance(producer, dict) or set(producer) != {
        "runId", "runAttempt", "jobId", "workflowPath", "actorId",
        "dispatchAttemptIdentity",
    }:
        raise ValueError("Delivery receipt producer fields are invalid.")
    for field in ("runId", "runAttempt", "jobId", "actorId"):
        require_positive_integer(
            producer.get(field), f"Delivery producer {field}"
        )
    dispatch_identity = producer.get("dispatchAttemptIdentity")
    if (
        producer.get("workflowPath") != stage.get("workflow")
        or not isinstance(dispatch_identity, str)
        or SHA256.fullmatch(dispatch_identity) is None
    ):
        raise ValueError("Delivery receipt producer identity is invalid.")
    upstream = receipt.get("upstreamReceipts")
    expected_upstream = stage.get("upstreamStages")
    if not isinstance(upstream, list) or not isinstance(expected_upstream, list):
        raise ValueError("Delivery upstream receipt chain is invalid.")
    if [item.get("stage") for item in upstream if isinstance(item, dict)] != expected_upstream:
        raise ValueError("Delivery upstream stages do not match preparation.")
    for item in upstream:
        if not isinstance(item, dict) or set(item) != {"stage", "sha256"}:
            raise ValueError("Delivery upstream receipt fields are invalid.")
        digest = item.get("sha256")
        if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
            raise ValueError("Delivery upstream receipt digest is invalid.")
    return stage, producer


def validate_toolchain_identity(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {"id", "manifestSha256"}:
        raise ValueError("Delivery toolchain identity fields are invalid.")
    if any(
        not isinstance(value.get(field), str)
        or SHA256.fullmatch(str(value[field])) is None
        for field in ("id", "manifestSha256")
    ):
        raise ValueError("Delivery toolchain identity is invalid.")
    return value


def validate_site_identity(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "identitySha256", "indexHtmlSha256",
    }:
        raise ValueError("Delivery site identity fields are invalid.")
    if any(
        not isinstance(value.get(field), str)
        or SHA256.fullmatch(str(value[field])) is None
        for field in ("identitySha256", "indexHtmlSha256")
    ):
        raise ValueError("Delivery site identity is invalid.")
    return value


def validate_candidate_receipt(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_sha256: str,
    expected_kind: str,
) -> None:
    common = {
        "schemaVersion", "kind", "stage", "preparationSha256",
        "stageIdentity", "repository", "sourceCommit", "infrastructureCommit",
        "workflowCommit", "workflowRef", "stateAnchor", "upstreamReceipts",
        "producer", "archive", "artifact",
    }
    kind_fields = {
        "playground-toolchain-candidate": {"toolchain"},
        "playground-site-candidate": {
            "toolchainCandidateSha256", "site", "toolchain",
        },
        "website-site-candidate": {"site"},
    }
    fields = kind_fields.get(expected_kind)
    if fields is None or set(receipt) != common | fields:
        raise ValueError("Delivery candidate receipt fields are invalid.")
    _, producer = validate_delivery_envelope(
        receipt, preparation, preparation_sha256, expected_kind
    )
    validate_archive_descriptor(receipt.get("archive"))
    validate_artifact_descriptor(
        receipt.get("artifact"),
        repository=str(receipt["repository"]),
        producer=producer,
    )
    artifact = receipt["artifact"]
    assert isinstance(artifact, dict)
    if artifact.get("artifactName") != delivery_candidate_payload_artifact_name(
        expected_kind, int(producer["runId"]), int(producer["runAttempt"])
    ):
        raise ValueError("Delivery candidate payload artifact name is invalid.")
    if expected_kind.startswith("playground-"):
        validate_toolchain_identity(receipt.get("toolchain"))
    if expected_kind.endswith("site-candidate"):
        validate_site_identity(receipt.get("site"))
    if expected_kind == "playground-site-candidate":
        digest = receipt.get("toolchainCandidateSha256")
        if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
            raise ValueError("Playground toolchain candidate digest is invalid.")


def validate_evidence_descriptor(value: object) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "artifactId", "artifactName", "fileName", "sha256",
    }:
        raise ValueError("Delivery evidence descriptor fields are invalid.")
    require_positive_integer(value.get("artifactId"), "Delivery evidence artifact ID")
    for field in ("artifactName", "fileName"):
        require_safe_leaf(value.get(field), "Delivery evidence name")
    digest = value.get("sha256")
    if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
        raise ValueError("Delivery evidence digest is invalid.")
    return value


def delivery_evidence_artifact_name(
    kind: str,
    run_id: int,
    run_attempt: int,
    job_id: int,
    *,
    browser: str | None = None,
) -> str:
    for value, label in (
        (run_id, "Delivery evidence run ID"),
        (run_attempt, "Delivery evidence run attempt"),
        (job_id, "Delivery evidence job ID"),
    ):
        require_positive_integer(value, label)
    if kind == "playground-completion":
        if browser not in {"chromium", "firefox", "webkit"}:
            raise ValueError("Playground evidence browser is invalid.")
        lane = f"playground-{browser}"
    elif kind == "website-completion":
        if browser is not None:
            raise ValueError("Website evidence cannot identify a browser lane.")
        lane = "website"
    else:
        raise ValueError("Delivery evidence kind is invalid.")
    return f"delivery-evidence-{lane}-{run_id}-{run_attempt}-{job_id}"


def validate_deployment(
    value: object,
    *,
    producer: dict[str, object],
    expected_url: str,
) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "id", "environment", "url", "runId", "runAttempt", "jobId",
    }:
        raise ValueError("Delivery deployment fields are invalid.")
    for field in ("runId", "runAttempt", "jobId"):
        require_positive_integer(value.get(field), f"Delivery deployment {field}")
    require_positive_integer(value.get("id"), "Delivery deployment ID")
    if (
        value.get("environment") != "github-pages"
        or value.get("url") != expected_url
        or value.get("runId") != producer.get("runId")
        or value.get("runAttempt") != producer.get("runAttempt")
    ):
        raise ValueError("Delivery deployment identity is invalid.")
    return value


def validate_completion_receipt(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_sha256: str,
    expected_kind: str,
) -> None:
    common = {
        "schemaVersion", "kind", "stage", "preparationSha256",
        "stageIdentity", "repository", "sourceCommit", "infrastructureCommit",
        "workflowCommit", "workflowRef", "stateAnchor", "upstreamReceipts",
        "producer", "status", "deployment",
    }
    fields = {
        "playground-completion": {
            "toolchainCandidateSha256", "siteCandidateSha256", "liveChecks",
        },
        "website-completion": {"siteCandidateSha256", "liveCheck"},
    }.get(expected_kind)
    if fields is None or set(receipt) != common | fields:
        raise ValueError("Delivery completion receipt fields are invalid.")
    _, producer = validate_delivery_envelope(
        receipt, preparation, preparation_sha256, expected_kind
    )
    if receipt.get("status") != "PASS":
        raise ValueError("Delivery completion status is not PASS.")
    for field in (
        ("toolchainCandidateSha256", "siteCandidateSha256")
        if expected_kind == "playground-completion"
        else ("siteCandidateSha256",)
    ):
        digest = receipt.get(field)
        if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
            raise ValueError("Delivery completion candidate digest is invalid.")
    expected_url = (
        "https://playground.netwasm.com/"
        if expected_kind == "playground-completion"
        else "https://www.netwasm.com/"
    )
    validate_deployment(
        receipt.get("deployment"), producer=producer, expected_url=expected_url
    )
    if expected_kind == "playground-completion":
        checks = receipt.get("liveChecks")
        if not isinstance(checks, list) or len(checks) != 3:
            raise ValueError("Playground completion requires three live checks.")
        expected_browsers = ["chromium", "firefox", "webkit"]
        if sorted(
            item.get("browser") for item in checks if isinstance(item, dict)
        ) != expected_browsers:
            raise ValueError("Playground live browser set is invalid.")
        for check in checks:
            if not isinstance(check, dict) or set(check) != {
                "browser", "status", "jobId", "siteIdentitySha256",
                "toolchainId", "toolchainManifestSha256", "evidence",
            }:
                raise ValueError("Playground live-check fields are invalid.")
            require_positive_integer(check.get("jobId"), "Playground live-check job ID")
            if check.get("status") != "PASS":
                raise ValueError("Playground live check did not pass.")
            for field in (
                "siteIdentitySha256", "toolchainId", "toolchainManifestSha256",
            ):
                digest = check.get(field)
                if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
                    raise ValueError("Playground live-check identity is invalid.")
            validate_evidence_descriptor(check.get("evidence"))
            expected_artifact = delivery_evidence_artifact_name(
                expected_kind,
                int(producer["runId"]),
                int(producer["runAttempt"]),
                int(check["jobId"]),
                browser=str(check["browser"]),
            )
            if (
                check["evidence"]["artifactName"] != expected_artifact
                or check["evidence"]["fileName"] != "delivery-evidence.json"
            ):
                raise ValueError("Playground live evidence name is invalid.")
        job_ids = [check["jobId"] for check in checks]
        artifact_ids = [check["evidence"]["artifactId"] for check in checks]
        if (
            len(set(job_ids)) != 3
            or len(set(artifact_ids)) != 3
            or producer["jobId"] in job_ids
            or receipt["deployment"]["jobId"] in job_ids
            or producer["jobId"] == receipt["deployment"]["jobId"]
        ):
            raise ValueError("Playground completion job identity is duplicated.")
        identity_fields = (
            "siteIdentitySha256", "toolchainId", "toolchainManifestSha256",
        )
        if any(
            len({check[field] for check in checks}) != 1
            for field in identity_fields
        ):
            raise ValueError("Playground live checks report contradictory identities.")
        return
    check = receipt.get("liveCheck")
    if not isinstance(check, dict) or set(check) != {
        "status", "jobId", "siteIdentitySha256", "playgroundCompletionSha256",
        "evidence",
    }:
        raise ValueError("Website live-check fields are invalid.")
    require_positive_integer(check.get("jobId"), "Website live-check job ID")
    if check.get("status") != "PASS":
        raise ValueError("Website live check did not pass.")
    for field in ("siteIdentitySha256", "playgroundCompletionSha256"):
        digest = check.get(field)
        if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
            raise ValueError("Website live-check identity is invalid.")
    validate_evidence_descriptor(check.get("evidence"))
    if (
        check["evidence"]["artifactName"]
        != delivery_evidence_artifact_name(
            expected_kind,
            int(producer["runId"]),
            int(producer["runAttempt"]),
            int(check["jobId"]),
        )
        or check["evidence"]["fileName"] != "delivery-evidence.json"
    ):
        raise ValueError("Website live evidence name is invalid.")
    upstream = receipt["upstreamReceipts"]
    if (
        len(upstream) != 1
        or upstream[0]["stage"] != "playground"
        or check["playgroundCompletionSha256"] != upstream[0]["sha256"]
    ):
        raise ValueError("Website live check does not match Playground completion.")
    if len({producer["jobId"], receipt["deployment"]["jobId"], check["jobId"]}) != 3:
        raise ValueError("Website completion job identity is duplicated.")


def validate_stage_receipt(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_sha256: str,
    expected_stage: str,
) -> None:
    if expected_stage in {item[0] for item in PREPARATION_STAGES[:6]}:
        validate_publication_receipt(
            receipt, preparation, preparation_sha256, expected_stage
        )
    elif expected_stage == "playground":
        validate_completion_receipt(
            receipt, preparation, preparation_sha256, "playground-completion"
        )
    elif expected_stage == "website":
        validate_completion_receipt(
            receipt, preparation, preparation_sha256, "website-completion"
        )
    else:
        raise ValueError(f"Release receipt stage is invalid: {expected_stage}.")


def validate_feed_receipt(
    receipt: dict[str, object],
    *,
    repository: str,
    source_commit: str,
    release_tag: str,
    candidate: dict[str, object],
) -> list[dict[str, object]]:
    required = {
        "schemaVersion", "status", "repository", "releaseVersion",
        "releaseTag", "sourceCommit", "feed", "packages",
    }
    if set(receipt) != required or receipt.get("schemaVersion") != 1:
        raise ValueError("Feed publication receipt fields are invalid.")
    expected = {
        "status": "PASS",
        "repository": repository,
        "releaseVersion": candidate.get("version"),
        "releaseTag": release_tag,
        "sourceCommit": source_commit,
        "feed": "https://api.nuget.org/v3/index.json",
    }
    for field, value in expected.items():
        if receipt.get(field) != value:
            raise ValueError(f"Feed publication receipt identity mismatch: {field}.")
    packages = receipt.get("packages")
    candidate_packages = candidate.get("packages")
    if not isinstance(packages, list) or not isinstance(candidate_packages, list):
        raise ValueError("Feed publication receipt has invalid packages.")
    expected_packages: dict[str, dict[str, object]] = {}
    for item in candidate_packages:
        if not isinstance(item, dict):
            raise ValueError("Candidate package inventory is invalid.")
        package_id = item.get("id")
        if not isinstance(package_id, str) or package_id in expected_packages:
            raise ValueError("Candidate package inventory is duplicated or invalid.")
        expected_packages[package_id] = item
    result: list[dict[str, object]] = []
    for package in packages:
        if not isinstance(package, dict) or set(package) != {
            "id", "version", "fileName", "candidateSha256",
            "normalizedPayloadSha256",
        }:
            raise ValueError("Feed publication receipt package fields are invalid.")
        package_id = package.get("id")
        expected_package = expected_packages.get(package_id)
        if (
            not isinstance(package_id, str)
            or not isinstance(expected_package, dict)
            or package.get("version") != candidate.get("version")
            or package.get("fileName") != expected_package.get("fileName")
            or package.get("candidateSha256") != expected_package.get("sha256")
            or not isinstance(package.get("normalizedPayloadSha256"), str)
            or SHA256.fullmatch(str(package["normalizedPayloadSha256"])) is None
        ):
            raise ValueError("Feed publication receipt package identity is invalid.")
        result.append(package)
    if [item["id"] for item in result] != sorted(expected_packages):
        raise ValueError("Feed publication receipt package set is incomplete or unsorted.")
    return result


def validate_publication_receipt(
    receipt: dict[str, object],
    preparation: dict[str, object],
    preparation_sha256: str,
    expected_stage: str,
) -> None:
    required = {
        "schemaVersion", "status", "stage", "repository", "version",
        "releaseTag", "sourceCommit", "preparationSha256", "candidate",
        "publication", "feed", "packages", "upstreamReceipts",
    }
    if set(receipt) != required or receipt.get("schemaVersion") != 1:
        raise ValueError("Publication receipt fields are invalid.")
    stage = preparation_stage(preparation, expected_stage)
    if (
        receipt.get("status") != "PASS"
        or receipt.get("stage") != expected_stage
        or receipt.get("repository") != stage.get("repository")
        or receipt.get("version") != (
            f"{preparation['version']}-preview.1"
            if expected_stage.endswith("-preview")
            else preparation["version"]
        )
        or receipt.get("releaseTag") != stage.get("ref")
        or receipt.get("sourceCommit") != stage.get("sourceCommit")
        or receipt.get("preparationSha256") != preparation_sha256
    ):
        raise ValueError("Publication receipt does not match its approved stage.")
    candidate = receipt.get("candidate")
    publication = receipt.get("publication")
    if not isinstance(candidate, dict) or set(candidate) != {
        "trainManifestSha256", "bundleSha256", "packageReceiptSha256",
        "producingRunId", "producingRunAttempt", "artifactName",
    }:
        raise ValueError("Publication receipt candidate coordinates are invalid.")
    if any(
        not isinstance(candidate.get(field), str)
        or SHA256.fullmatch(str(candidate[field])) is None
        for field in ("trainManifestSha256", "bundleSha256", "packageReceiptSha256")
    ):
        raise ValueError("Publication receipt candidate digests are invalid.")
    if (
        not isinstance(candidate.get("producingRunId"), str)
        or not str(candidate["producingRunId"]).isdigit()
        or not isinstance(candidate.get("producingRunAttempt"), str)
        or not str(candidate["producingRunAttempt"]).isdigit()
        or not isinstance(candidate.get("artifactName"), str)
        or not candidate["artifactName"]
    ):
        raise ValueError("Publication receipt candidate producer is invalid.")
    if not isinstance(publication, dict) or set(publication) != {
        "workflow", "infrastructureCommit", "runId", "runAttempt",
    }:
        raise ValueError("Publication receipt workflow coordinates are invalid.")
    if (
        publication.get("workflow") != stage.get("workflow")
        or publication.get("infrastructureCommit") != stage.get("infrastructureCommit")
        or not isinstance(publication.get("runId"), str)
        or not str(publication["runId"]).isdigit()
        or not isinstance(publication.get("runAttempt"), str)
        or not str(publication["runAttempt"]).isdigit()
    ):
        raise ValueError("Publication receipt workflow identity is invalid.")
    if receipt.get("feed") != "https://api.nuget.org/v3/index.json":
        raise ValueError("Publication receipt feed identity is invalid.")
    packages = receipt.get("packages")
    if not isinstance(packages, list) or not packages:
        raise ValueError("Publication receipt has no packages.")
    package_ids: list[str] = []
    for package in packages:
        if not isinstance(package, dict) or set(package) != {
            "id", "version", "fileName", "candidateSha256",
            "normalizedPayloadSha256",
        }:
            raise ValueError("Publication receipt package fields are invalid.")
        package_id = package.get("id")
        if (
            not isinstance(package_id, str)
            or not package_id
            or package.get("version") != receipt.get("version")
            or not isinstance(package.get("fileName"), str)
            or not package["fileName"]
            or not isinstance(package.get("candidateSha256"), str)
            or SHA256.fullmatch(str(package["candidateSha256"])) is None
            or not isinstance(package.get("normalizedPayloadSha256"), str)
            or SHA256.fullmatch(str(package["normalizedPayloadSha256"])) is None
        ):
            raise ValueError("Publication receipt package identity is invalid.")
        package_ids.append(package_id)
    if package_ids != sorted(set(package_ids)):
        raise ValueError("Publication receipt packages are duplicated or unsorted.")
    upstream = receipt.get("upstreamReceipts")
    expected_upstream = stage.get("upstreamStages")
    if not isinstance(upstream, list) or not isinstance(expected_upstream, list):
        raise ValueError("Publication receipt upstream chain is invalid.")
    if [item.get("stage") for item in upstream if isinstance(item, dict)] != expected_upstream:
        raise ValueError("Publication receipt upstream stages do not match preparation.")
    for item, upstream_name in zip(upstream, expected_upstream, strict=True):
        if not isinstance(item, dict) or set(item) != {
            "stage", "repository", "version", "releaseTag", "sha256",
        }:
            raise ValueError("Publication receipt upstream coordinates are invalid.")
        upstream_stage = preparation_stage(preparation, upstream_name)
        upstream_version = (
            f"{preparation['version']}-preview.1"
            if upstream_name.endswith("-preview")
            else preparation["version"]
        )
        if (
            item.get("repository") != upstream_stage.get("repository")
            or item.get("version") != upstream_version
            or item.get("releaseTag") != upstream_stage.get("ref")
            or not isinstance(item.get("sha256"), str)
            or SHA256.fullmatch(str(item["sha256"])) is None
        ):
            raise ValueError("Publication receipt upstream coordinates are invalid.")


def validate_publication_approval(
    *,
    preparation_path: Path,
    stage_name: str,
    train_path: Path,
    upstream_receipt_paths: list[Path],
    upstream_receipt_sha256: list[str],
) -> tuple[
    dict[str, object], str, dict[str, object], dict[str, object],
    dict[str, object], list[dict[str, object]],
]:
    if stage_name not in {item[0] for item in PREPARATION_STAGES[:6]}:
        raise ValueError("Only package stages produce NuGet publication receipts.")
    preparation, preparation_digest = read_preparation(preparation_path)
    stage = preparation_stage(preparation, stage_name)
    train = read_json(train_path)
    validate_train_structure(train)
    if train.get("schemaVersion") != 2:
        raise ValueError("Coordinated publication requires a schema-version-2 train.")
    if (
        train.get("repository") != stage.get("repository")
        or train.get("sourceCommit") != stage.get("sourceCommit")
        or train.get("preparationSha256") != preparation_digest
        or train.get("infrastructureCommit") != stage.get("infrastructureCommit")
    ):
        raise ValueError("Candidate train does not match the approved preparation stage.")
    repository_stages = [
        item for item in PREPARATION_STAGES[:6]
        if item[1] == stage.get("repository")
    ]
    if len(repository_stages) != 2:
        raise ValueError("Approved package repository does not have two release channels.")
    preview_stage = preparation_stage(preparation, repository_stages[0][0])
    stable_stage = preparation_stage(preparation, repository_stages[1][0])
    preview_candidate = train.get("preview")
    stable_candidate = train.get("stable")
    if (
        train.get("producingReleaseTag") != preview_stage.get("ref")
        or not isinstance(preview_candidate, dict)
        or preview_candidate.get("releaseTag") != preview_stage.get("ref")
        or not isinstance(stable_candidate, dict)
        or stable_candidate.get("releaseTag") != stable_stage.get("ref")
    ):
        raise ValueError("Candidate train tags do not match the approved preparation.")
    channel = "preview" if stage_name.endswith("-preview") else "stable"
    candidate = train.get(channel)
    if not isinstance(candidate, dict) or candidate.get("releaseTag") != stage.get("ref"):
        raise ValueError("Candidate train does not target the approved release.")
    expected_upstream = stage.get("upstreamStages")
    assert isinstance(expected_upstream, list)
    if (
        len(upstream_receipt_paths) != len(expected_upstream)
        or len(upstream_receipt_sha256) != len(expected_upstream)
    ):
        raise ValueError("Publication receipt upstream count does not match preparation.")
    current_train_digest = sha256(train_path)
    current_producer = {
        "trainManifestSha256": current_train_digest,
        "producingRunId": train["workflowRunId"],
        "producingRunAttempt": train["workflowRunAttempt"],
        "artifactName": train["artifactName"],
    }
    upstream: list[dict[str, object]] = []
    for expected_name, path, expected_digest in zip(
        expected_upstream, upstream_receipt_paths, upstream_receipt_sha256,
        strict=True,
    ):
        if SHA256.fullmatch(expected_digest) is None or sha256(path) != expected_digest:
            raise ValueError("Publication receipt upstream digest does not match dispatch.")
        value = read_json(path)
        validate_publication_receipt(
            value, preparation, preparation_digest, expected_name
        )
        if stage_name.endswith("-stable") and expected_name.endswith("-preview"):
            upstream_candidate = value.get("candidate")
            if not isinstance(upstream_candidate, dict) or any(
                upstream_candidate.get(field) != expected_value
                for field, expected_value in current_producer.items()
            ):
                raise ValueError(
                    "Stable publication does not match the retained preview train."
                )
        upstream.append({
            "stage": expected_name,
            "repository": value["repository"],
            "version": value["version"],
            "releaseTag": value["releaseTag"],
            "sha256": sha256(path),
        })
    return preparation, preparation_digest, stage, train, candidate, upstream


def create_publication_receipt(
    *,
    preparation_path: Path,
    stage_name: str,
    train_path: Path,
    feed_receipt_path: Path,
    publication_run_id: str,
    publication_run_attempt: str,
    upstream_receipt_paths: list[Path],
    upstream_receipt_sha256: list[str],
) -> dict[str, object]:
    (
        preparation,
        preparation_digest,
        stage,
        train,
        candidate,
        upstream,
    ) = validate_publication_approval(
        preparation_path=preparation_path,
        stage_name=stage_name,
        train_path=train_path,
        upstream_receipt_paths=upstream_receipt_paths,
        upstream_receipt_sha256=upstream_receipt_sha256,
    )
    current_train_digest = sha256(train_path)
    feed_receipt = read_json(feed_receipt_path)
    packages = validate_feed_receipt(
        feed_receipt,
        repository=str(stage["repository"]),
        source_commit=str(stage["sourceCommit"]),
        release_tag=str(stage["ref"]),
        candidate=candidate,
    )
    if not publication_run_id.isdigit() or not publication_run_attempt.isdigit():
        raise ValueError("Publication workflow run coordinates are invalid.")
    result = {
        "schemaVersion": PUBLICATION_RECEIPT_SCHEMA_VERSION,
        "status": "PASS",
        "stage": stage_name,
        "repository": stage["repository"],
        "version": candidate["version"],
        "releaseTag": stage["ref"],
        "sourceCommit": stage["sourceCommit"],
        "preparationSha256": preparation_digest,
        "candidate": {
            "trainManifestSha256": current_train_digest,
            "bundleSha256": candidate["bundleSha256"],
            "packageReceiptSha256": candidate["receiptSha256"],
            "producingRunId": train["workflowRunId"],
            "producingRunAttempt": train["workflowRunAttempt"],
            "artifactName": train["artifactName"],
        },
        "publication": {
            "workflow": stage["workflow"],
            "infrastructureCommit": stage["infrastructureCommit"],
            "runId": publication_run_id,
            "runAttempt": publication_run_attempt,
        },
        "feed": feed_receipt["feed"],
        "packages": packages,
        "upstreamReceipts": upstream,
    }
    validate_publication_receipt(
        result, preparation, preparation_digest, stage_name
    )
    return result


def package_summary(
    manifest: dict[str, object], receipt: dict[str, object]
) -> list[dict[str, object]]:
    for field in ("repository", "releaseVersion", "releaseTag", "sourceCommit"):
        if receipt.get(field) != manifest.get(field):
            raise ValueError(f"Release receipt {field} does not match its manifest.")
    if receipt.get("schemaVersion") != 1 or receipt.get("status") != "PASS":
        raise ValueError("Release receipt is not a passing schema-version-1 receipt.")
    packages = receipt.get("packages")
    if not isinstance(packages, list) or not packages:
        raise ValueError("Release receipt has no packages.")
    expected_ids = manifest.get("packages")
    if not isinstance(expected_ids, list):
        raise ValueError("Release manifest has no package allowlist.")
    result: list[dict[str, object]] = []
    for package in packages:
        if not isinstance(package, dict):
            raise ValueError("Release receipt contains an invalid package entry.")
        summary = {
            name: package.get(name)
            for name in ("id", "version", "fileName", "size", "sha256")
        }
        if (
            not isinstance(summary["id"], str)
            or not isinstance(summary["version"], str)
            or not isinstance(summary["fileName"], str)
            or not isinstance(summary["size"], int)
            or isinstance(summary["size"], bool)
            or summary["size"] < 0
            or not isinstance(summary["sha256"], str)
            or re.fullmatch(r"[0-9a-f]{64}", summary["sha256"]) is None
        ):
            raise ValueError("Release receipt contains invalid package identity data.")
        result.append(summary)
    if [item["id"] for item in result] != sorted(expected_ids):
        raise ValueError("Release receipt packages do not match the sorted manifest allowlist.")
    return result


def canonical_entry(name: str) -> zipfile.ZipInfo:
    entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
    entry.create_system = 3
    entry.external_attr = (stat.S_IFREG | 0o644) << 16
    entry.compress_type = zipfile.ZIP_STORED
    return entry


def create_bundle(
    manifest_path: Path,
    receipt_path: Path,
    packages_root: Path,
    output: Path,
) -> dict[str, object]:
    if output.exists():
        raise ValueError(f"Release-train bundle already exists: {output}")
    manifest = read_json(manifest_path)
    receipt = read_json(receipt_path)
    packages = package_summary(manifest, receipt)
    expected_files = {str(item["fileName"]) for item in packages}
    actual_files = {path.name for path in packages_root.glob("*.nupkg")}
    if actual_files != expected_files:
        raise ValueError(
            f"Package files do not match the receipt; expected={sorted(expected_files)}, "
            f"actual={sorted(actual_files)}"
        )
    for item in packages:
        path = packages_root / str(item["fileName"])
        if path.stat().st_size != item["size"] or sha256(path) != item["sha256"]:
            raise ValueError(f"Package does not match its receipt: {path.name}")

    checksums = "".join(
        f"{item['sha256']}  packages/{item['fileName']}\n" for item in packages
    ).encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "x", allowZip64=True) as bundle:
        for name, path in (
            ("release-manifest.json", manifest_path),
            ("release-package-receipt.json", receipt_path),
        ):
            bundle.writestr(canonical_entry(name), path.read_bytes())
        bundle.writestr(canonical_entry("SHA256SUMS"), checksums)
        for item in packages:
            path = packages_root / str(item["fileName"])
            entry = canonical_entry(f"packages/{path.name}")
            with path.open("rb") as source, bundle.open(
                entry, "w", force_zip64=True
            ) as destination:
                shutil.copyfileobj(source, destination, length=1024 * 1024)
    return inspect_bundle(output)


def safe_bundle_names(bundle: zipfile.ZipFile) -> list[str]:
    names = bundle.namelist()
    if len(names) != len(set(names)):
        raise ValueError("Release-train bundle contains duplicate entries.")
    total = 0
    for entry in bundle.infolist():
        path = PurePosixPath(entry.filename)
        if (
            path.is_absolute()
            or not path.parts
            or any(part in {"", ".", ".."} for part in path.parts)
            or entry.is_dir()
        ):
            raise ValueError(f"Release-train bundle contains an unsafe entry: {entry.filename}")
        if entry.file_size > MAX_ENTRY_BYTES:
            raise ValueError(f"Release-train bundle entry is too large: {entry.filename}")
        total += entry.file_size
    if total > MAX_BUNDLE_CONTENT_BYTES:
        raise ValueError("Release-train bundle content exceeds the size limit.")
    return names


def inspect_bundle(path: Path) -> dict[str, object]:
    with zipfile.ZipFile(path) as bundle:
        names = safe_bundle_names(bundle)
        required = {"release-manifest.json", "release-package-receipt.json", "SHA256SUMS"}
        if not required <= set(names):
            raise ValueError("Release-train bundle is missing identity documents.")
        manifest = json.loads(bundle.read("release-manifest.json"))
        receipt = json.loads(bundle.read("release-package-receipt.json"))
        if not isinstance(manifest, dict) or not isinstance(receipt, dict):
            raise ValueError("Release-train bundle identity documents are invalid.")
        packages = package_summary(manifest, receipt)
        expected = required | {f"packages/{item['fileName']}" for item in packages}
        if set(names) != expected:
            raise ValueError("Release-train bundle entries do not match its receipt.")
        expected_sums = "".join(
            f"{item['sha256']}  packages/{item['fileName']}\n" for item in packages
        ).encode("utf-8")
        if bundle.read("SHA256SUMS") != expected_sums:
            raise ValueError("Release-train bundle checksum index does not match its receipt.")
        for item in packages:
            entry = bundle.getinfo(f"packages/{item['fileName']}")
            with bundle.open(entry) as stream:
                digest = sha256_stream(stream)
            if entry.file_size != item["size"] or digest != item["sha256"]:
                raise ValueError(
                    f"Release-train bundle package does not match its receipt: {item['fileName']}"
                )
        return {
            "version": manifest["releaseVersion"],
            "sourceCommit": manifest["sourceCommit"],
            "producingReleaseTag": manifest["releaseTag"],
            "bundleFile": path.name,
            "bundleSize": path.stat().st_size,
            "bundleSha256": sha256(path),
            "manifestSha256": hashlib.sha256(
                bundle.read("release-manifest.json")
            ).hexdigest(),
            "receiptSha256": hashlib.sha256(
                bundle.read("release-package-receipt.json")
            ).hexdigest(),
            "packages": packages,
        }


def extract_bundle(path: Path, destination: Path) -> dict[str, object]:
    summary = inspect_bundle(path)
    if destination.exists() and any(destination.iterdir()):
        raise ValueError(f"Release-train extraction destination is not empty: {destination}")
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path) as bundle:
        for name in ("release-manifest.json", "release-package-receipt.json"):
            (destination / name).write_bytes(bundle.read(name))
        packages = destination / "packages"
        packages.mkdir()
        for item in summary["packages"]:
            file_name = str(item["fileName"])
            with bundle.open(f"packages/{file_name}") as source, (
                packages / file_name
            ).open("xb") as target:
                shutil.copyfileobj(source, target, length=1024 * 1024)
    return summary


def create_train_manifest(
    *,
    repository: str,
    source_commit: str,
    producing_tag: str,
    preview_tag: str,
    stable_tag: str,
    run_id: str,
    artifact_name: str,
    preview_bundle: Path,
    stable_bundle: Path,
    toolchain: Path,
    preparation_sha256: str | None = None,
    infrastructure_commit: str | None = None,
    run_attempt: str | None = None,
) -> dict[str, object]:
    if COMMIT.fullmatch(source_commit) is None:
        raise ValueError("Release-train source commit is invalid.")
    preview = inspect_bundle(preview_bundle)
    stable = inspect_bundle(stable_bundle)
    for channel in (preview, stable):
        if channel["sourceCommit"] != source_commit:
            raise ValueError("Release-train bundle source commit does not match the train.")
        if channel["producingReleaseTag"] != producing_tag:
            raise ValueError("Release-train bundle was not qualified against the producing tag.")
    preview["releaseTag"] = preview_tag
    stable["releaseTag"] = stable_tag
    toolchain_value = read_json(toolchain)
    optional = (preparation_sha256, infrastructure_commit, run_attempt)
    if any(value is not None for value in optional) and any(
        value is None for value in optional
    ):
        raise ValueError("Coordinated release-train identity must be supplied together.")
    result: dict[str, object] = {
        "schemaVersion": 2 if preparation_sha256 is not None else SCHEMA_VERSION,
        "repository": repository,
        "sourceCommit": source_commit,
        "producingReleaseTag": producing_tag,
        "workflowRunId": str(run_id),
        "artifactName": artifact_name,
        "toolchainSha256": sha256(toolchain),
        "nativeToolchain": toolchain_value,
        "preview": preview,
        "stable": stable,
    }
    if preparation_sha256 is not None:
        if SHA256.fullmatch(preparation_sha256) is None:
            raise ValueError("Release-train preparation digest is invalid.")
        if infrastructure_commit is None or COMMIT.fullmatch(infrastructure_commit) is None:
            raise ValueError("Release-train infrastructure commit is invalid.")
        if run_attempt is None or not run_attempt.isdigit() or int(run_attempt) < 1:
            raise ValueError("Release-train producing run attempt is invalid.")
        result.update({
            "preparationSha256": preparation_sha256,
            "infrastructureCommit": infrastructure_commit,
            "workflowRunAttempt": run_attempt,
        })
    return result


def train_required_fields(schema_version: object) -> set[str]:
    fields = {
        "schemaVersion", "repository", "sourceCommit", "producingReleaseTag",
        "workflowRunId", "artifactName", "toolchainSha256",
        "nativeToolchain", "preview", "stable",
    }
    if schema_version == 2:
        fields.update({
            "preparationSha256", "infrastructureCommit", "workflowRunAttempt",
        })
    elif schema_version != SCHEMA_VERSION:
        raise ValueError("Release-train manifest schema version is unsupported.")
    return fields


def validate_train_structure(train: dict[str, object]) -> None:
    if set(train) != train_required_fields(train.get("schemaVersion")):
        raise ValueError("Release-train manifest fields do not match its schema version.")
    repository = train.get("repository")
    source_commit = train.get("sourceCommit")
    producing_tag = train.get("producingReleaseTag")
    workflow_run_id = train.get("workflowRunId")
    artifact_name = train.get("artifactName")
    toolchain_digest = train.get("toolchainSha256")
    if (
        not isinstance(repository, str)
        or not repository
        or not isinstance(source_commit, str)
        or COMMIT.fullmatch(source_commit) is None
        or not isinstance(producing_tag, str)
        or not producing_tag
        or not isinstance(workflow_run_id, str)
        or not workflow_run_id.isdigit()
        or int(workflow_run_id) < 1
        or not isinstance(artifact_name, str)
        or not artifact_name
        or not isinstance(toolchain_digest, str)
        or SHA256.fullmatch(toolchain_digest) is None
        or not isinstance(train.get("nativeToolchain"), dict)
    ):
        raise ValueError("Release-train root identity is invalid.")

    candidate_fields = {
        "version", "sourceCommit", "producingReleaseTag", "bundleFile",
        "bundleSize", "bundleSha256", "manifestSha256", "receiptSha256",
        "packages", "releaseTag",
    }
    versions: dict[str, str] = {}
    for channel in ("preview", "stable"):
        candidate = train.get(channel)
        if not isinstance(candidate, dict) or set(candidate) != candidate_fields:
            raise ValueError(f"Release-train {channel} candidate fields are invalid.")
        version = candidate.get("version")
        bundle_file = candidate.get("bundleFile")
        bundle_size = candidate.get("bundleSize")
        if (
            not isinstance(version, str)
            or not version
            or candidate.get("sourceCommit") != source_commit
            or candidate.get("producingReleaseTag") != producing_tag
            or not isinstance(candidate.get("releaseTag"), str)
            or not candidate["releaseTag"]
            or not isinstance(bundle_file, str)
            or not bundle_file
            or Path(bundle_file).name != bundle_file
            or not isinstance(bundle_size, int)
            or isinstance(bundle_size, bool)
            or bundle_size < 0
            or any(
                not isinstance(candidate.get(field), str)
                or SHA256.fullmatch(str(candidate[field])) is None
                for field in ("bundleSha256", "manifestSha256", "receiptSha256")
            )
        ):
            raise ValueError(f"Release-train {channel} candidate identity is invalid.")
        packages = candidate.get("packages")
        if not isinstance(packages, list) or not packages:
            raise ValueError(f"Release-train {channel} candidate has no packages.")
        package_ids: list[str] = []
        for package in packages:
            if not isinstance(package, dict) or set(package) != {
                "id", "version", "fileName", "size", "sha256",
            }:
                raise ValueError(
                    f"Release-train {channel} candidate package fields are invalid."
                )
            package_id = package.get("id")
            file_name = package.get("fileName")
            size = package.get("size")
            digest = package.get("sha256")
            if (
                not isinstance(package_id, str)
                or not package_id
                or package.get("version") != version
                or not isinstance(file_name, str)
                or not file_name
                or Path(file_name).name != file_name
                or not isinstance(size, int)
                or isinstance(size, bool)
                or size < 0
                or not isinstance(digest, str)
                or SHA256.fullmatch(digest) is None
            ):
                raise ValueError(
                    f"Release-train {channel} candidate package identity is invalid."
                )
            package_ids.append(package_id)
        if package_ids != sorted(set(package_ids)):
            raise ValueError(
                f"Release-train {channel} candidate packages are duplicated or unsorted."
            )
        versions[channel] = version
    if (
        versions["preview"] != f"{versions['stable']}-preview.1"
        or VERSION.fullmatch(versions["stable"]) is None
    ):
        raise ValueError("Release-train preview and stable versions are inconsistent.")
    if train.get("schemaVersion") != 2:
        return
    preparation_digest = train.get("preparationSha256")
    infrastructure_commit = train.get("infrastructureCommit")
    run_attempt = train.get("workflowRunAttempt")
    if not isinstance(preparation_digest, str) or SHA256.fullmatch(preparation_digest) is None:
        raise ValueError("Release-train preparation digest is invalid.")
    if not isinstance(infrastructure_commit, str) or COMMIT.fullmatch(infrastructure_commit) is None:
        raise ValueError("Release-train infrastructure commit is invalid.")
    if not isinstance(run_attempt, str) or not run_attempt.isdigit() or int(run_attempt) < 1:
        raise ValueError("Release-train producing run attempt is invalid.")


def verify_train(
    train: dict[str, object],
    bundle: Path,
    channel: str,
    repository: str,
    source_commit: str,
    release_tag: str,
) -> dict[str, object]:
    validate_train_structure(train)
    if train.get("repository") != repository or train.get("sourceCommit") != source_commit:
        raise ValueError("Release train does not match the repository source release.")
    if channel not in {"preview", "stable"}:
        raise ValueError(f"Unsupported release-train channel: {channel}")
    expected = train[channel]
    if not isinstance(expected, dict) or expected.get("releaseTag") != release_tag:
        raise ValueError("Release train does not target this GitHub Release tag.")
    actual = inspect_bundle(bundle)
    for name in (
        "version", "sourceCommit", "producingReleaseTag", "bundleFile", "bundleSize",
        "bundleSha256", "manifestSha256", "receiptSha256", "packages",
    ):
        if actual.get(name) != expected.get(name):
            raise ValueError(f"Release-train {channel} bundle mismatch: {name}")
    return actual


def verify_train_identity(
    train: dict[str, object],
    repository: str,
    source_commit: str,
    producing_tag: str,
    preview_tag: str,
    stable_tag: str,
    expected_artifact_name: str,
) -> tuple[str, str]:
    validate_train_structure(train)
    expected = {
        "repository": repository,
        "sourceCommit": source_commit,
        "producingReleaseTag": producing_tag,
    }
    for name, value in expected.items():
        if train.get(name) != value:
            raise ValueError(f"Release-train identity mismatch: {name}")
    for channel, tag in (("preview", preview_tag), ("stable", stable_tag)):
        value = train.get(channel)
        if not isinstance(value, dict) or value.get("releaseTag") != tag:
            raise ValueError(f"Release-train identity mismatch: {channel} tag")
    run_id = train.get("workflowRunId")
    artifact_name = train.get("artifactName")
    if (
        not isinstance(run_id, str)
        or not run_id.isdigit()
        or not isinstance(artifact_name, str)
        or artifact_name != expected_artifact_name
    ):
        raise ValueError("Release-train producer coordinates are invalid.")
    return run_id, artifact_name



def effective_mode(resolved_mode: str, retained_manifest_count: int) -> str:
    if resolved_mode not in {"build", "promote"}:
        raise ValueError(f"Unsupported resolved release mode: {resolved_mode}")
    if retained_manifest_count not in {0, 1}:
        raise ValueError("Preview release has an ambiguous retained train manifest.")
    if resolved_mode == "promote":
        if retained_manifest_count != 1:
            raise ValueError("Stable promotion requires the retained preview train manifest.")
        return "promote"
    return "resume" if retained_manifest_count == 1 else "build"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    bundle = subparsers.add_parser("bundle")
    bundle.add_argument("--manifest", type=Path, required=True)
    bundle.add_argument("--receipt", type=Path, required=True)
    bundle.add_argument("--packages", type=Path, required=True)
    bundle.add_argument("--output", type=Path, required=True)
    create = subparsers.add_parser("create")
    create.add_argument("--repository", required=True)
    create.add_argument("--source-commit", required=True)
    create.add_argument("--producing-tag", required=True)
    create.add_argument("--preview-tag", required=True)
    create.add_argument("--stable-tag", required=True)
    create.add_argument("--run-id", required=True)
    create.add_argument("--artifact-name", required=True)
    create.add_argument("--preview-bundle", type=Path, required=True)
    create.add_argument("--stable-bundle", type=Path, required=True)
    create.add_argument("--toolchain", type=Path, required=True)
    create.add_argument("--preparation-sha256")
    create.add_argument("--infrastructure-commit")
    create.add_argument("--run-attempt")
    create.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify")
    verify.add_argument("--train", type=Path, required=True)
    verify.add_argument("--bundle", type=Path, required=True)
    verify.add_argument("--channel", choices=("preview", "stable"), required=True)
    verify.add_argument("--repository", required=True)
    verify.add_argument("--source-commit", required=True)
    verify.add_argument("--release-tag", required=True)
    identity = subparsers.add_parser("identity")
    identity.add_argument("--train", type=Path, required=True)
    identity.add_argument("--repository", required=True)
    identity.add_argument("--source-commit", required=True)
    identity.add_argument("--producing-tag", required=True)
    identity.add_argument("--preview-tag", required=True)
    identity.add_argument("--stable-tag", required=True)
    identity.add_argument("--artifact-name", required=True)
    identity.add_argument("--github-output", type=Path)
    state = subparsers.add_parser("state")
    state.add_argument("--resolved-mode", required=True)
    state.add_argument("--retained-manifest-count", required=True, type=int)
    state.add_argument("--github-output", required=True, type=Path)
    preparation = subparsers.add_parser("preparation")
    preparation.add_argument("--manifest", type=Path, required=True)
    preparation.add_argument("--stage")
    preparation.add_argument("--github-output", type=Path)
    publication = subparsers.add_parser("publication")
    publication.add_argument("--preparation", type=Path, required=True)
    publication.add_argument("--stage", required=True)
    publication.add_argument("--train", type=Path, required=True)
    publication.add_argument("--feed-receipt", type=Path, required=True)
    publication.add_argument("--publication-run-id", required=True)
    publication.add_argument("--publication-run-attempt", required=True)
    publication.add_argument(
        "--upstream-receipt", type=Path, action="append", default=[]
    )
    publication.add_argument(
        "--upstream-receipt-sha256", action="append", default=[]
    )
    publication.add_argument("--output", type=Path, required=True)
    approval = subparsers.add_parser("approval")
    approval.add_argument("--preparation", type=Path, required=True)
    approval.add_argument("--stage", required=True)
    approval.add_argument("--train", type=Path, required=True)
    approval.add_argument(
        "--upstream-receipt", type=Path, action="append", default=[]
    )
    approval.add_argument(
        "--upstream-receipt-sha256", action="append", default=[]
    )
    extract = subparsers.add_parser("extract")
    extract.add_argument("--bundle", type=Path, required=True)
    extract.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()

    if arguments.command == "bundle":
        summary = create_bundle(
            arguments.manifest, arguments.receipt, arguments.packages, arguments.output
        )
        print(f"Created {summary['bundleFile']} ({summary['bundleSize']:,} bytes).")
        return 0
    if arguments.command == "create":
        train = create_train_manifest(
            repository=arguments.repository,
            source_commit=arguments.source_commit,
            producing_tag=arguments.producing_tag,
            preview_tag=arguments.preview_tag,
            stable_tag=arguments.stable_tag,
            run_id=arguments.run_id,
            artifact_name=arguments.artifact_name,
            preview_bundle=arguments.preview_bundle,
            stable_bundle=arguments.stable_bundle,
            toolchain=arguments.toolchain,
            preparation_sha256=arguments.preparation_sha256,
            infrastructure_commit=arguments.infrastructure_commit,
            run_attempt=arguments.run_attempt,
        )
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(json.dumps(train, indent=2) + "\n", encoding="utf-8")
        return 0
    if arguments.command == "extract":
        summary = extract_bundle(arguments.bundle, arguments.output)
        print(f"Extracted release train {summary['version']}.")
        return 0
    if arguments.command == "state":
        mode = effective_mode(
            arguments.resolved_mode, arguments.retained_manifest_count
        )
        with arguments.github_output.open("a", encoding="utf-8") as output:
            output.write(f"mode={mode}\n")
        return 0
    if arguments.command == "preparation":
        preparation_value, digest = read_preparation(arguments.manifest)
        stage_value = None
        if arguments.stage is not None:
            stage_value = preparation_stage(preparation_value, arguments.stage)
        if arguments.github_output is not None:
            with arguments.github_output.open("a", encoding="utf-8") as stream:
                stream.write(f"preparation_sha256={digest}\n")
                stream.write(f"version={preparation_value['version']}\n")
                if stage_value is not None:
                    for field in (
                        "repository", "workflow", "sourceCommit",
                        "infrastructureCommit", "workflowCommit", "workflowRef",
                        "ref", "releaseId",
                    ):
                        output_name = re.sub(r"(?<!^)(?=[A-Z])", "_", field).lower()
                        stream.write(f"{output_name}={stage_value[field]}\n")
        print(
            f"Verified release preparation {digest} "
            f"for version {preparation_value['version']}."
        )
        return 0
    if arguments.command == "publication":
        receipt = create_publication_receipt(
            preparation_path=arguments.preparation,
            stage_name=arguments.stage,
            train_path=arguments.train,
            feed_receipt_path=arguments.feed_receipt,
            publication_run_id=arguments.publication_run_id,
            publication_run_attempt=arguments.publication_run_attempt,
            upstream_receipt_paths=arguments.upstream_receipt,
            upstream_receipt_sha256=arguments.upstream_receipt_sha256,
        )
        if arguments.output.exists():
            raise ValueError(f"Publication receipt already exists: {arguments.output}")
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(
            json.dumps(receipt, indent=2) + "\n", encoding="utf-8"
        )
        print(
            f"Recorded publication receipt for {receipt['stage']} "
            f"at {sha256(arguments.output)}."
        )
        return 0
    if arguments.command == "approval":
        validate_publication_approval(
            preparation_path=arguments.preparation,
            stage_name=arguments.stage,
            train_path=arguments.train,
            upstream_receipt_paths=arguments.upstream_receipt,
            upstream_receipt_sha256=arguments.upstream_receipt_sha256,
        )
        print(f"Verified coordinated publication approval for {arguments.stage}.")
        return 0
    train = read_json(arguments.train)
    if arguments.command == "identity":
        run_id, artifact_name = verify_train_identity(
            train,
            arguments.repository,
            arguments.source_commit,
            arguments.producing_tag,
            arguments.preview_tag,
            arguments.stable_tag,
            arguments.artifact_name,
        )
        if arguments.github_output is not None:
            with arguments.github_output.open("a", encoding="utf-8") as stream:
                stream.write(f"producing_run_id={run_id}\n")
                stream.write(f"artifact_name={artifact_name}\n")
        print(f"Verified release train from workflow run {run_id}.")
        return 0
    summary = verify_train(
        train, arguments.bundle, arguments.channel, arguments.repository,
        arguments.source_commit, arguments.release_tag,
    )
    print(f"Verified {arguments.channel} release train {summary['version']}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
