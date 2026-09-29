#!/usr/bin/env python3

from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


MODES = ("None", "O0", "O1", "O2", "O3", "Os", "Oz")
CANONICAL_FLAGS = {
    "None": None,
    "O0": "-O0",
    "O1": "-O1",
    "O2": "-O2",
    "O3": "-O3",
    "Os": "-Os",
    "Oz": "-Oz",
}
OPTIMIZATION_FLAGS = frozenset(flag for flag in CANONICAL_FLAGS.values() if flag)
CANDIDATE_PACKAGE_IDS = (
    "NetWasm.Compiler.Tasks",
    "NetWasm.Hosting",
    "NetWasm.Hosting.Build",
    "NetWasm.Ref",
    "NetWasm.Runtime.Pack",
    "NetWasm.Runtime.Wasm32",
    "NetWasm.Sdk",
    "NetWasm.Templates",
    "NetWasm.Toolchain",
)


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Publish one NetWasm application with every optimization mode and "
            "verify the runtime and final optimizer policy."
        )
    )
    parser.add_argument("package_directory", type=Path)
    parser.add_argument("version")
    parser.add_argument(
        "--host-tools-version",
        help="Released host-tools version when it differs from the candidate version.",
    )
    parser.add_argument(
        "--artifact-directory",
        type=Path,
        help="New directory in which to retain the complete qualification evidence.",
    )
    parser.add_argument(
        "--receipt",
        type=Path,
        help="Write the path-free qualification receipt to this file.",
    )
    return parser.parse_args()


def run(
    arguments: list[str],
    *,
    environment: dict[str, str],
    cwd: Path,
    stdout_path: Path,
) -> str:
    stdout_path.parent.mkdir(parents=True, exist_ok=True)
    with stdout_path.open("w", encoding="utf-8") as output:
        completed = subprocess.run(
            arguments,
            cwd=cwd,
            env=environment,
            stdout=output,
            stderr=subprocess.STDOUT,
            text=True,
            check=False,
        )
    if completed.returncode != 0:
        raise RuntimeError(
            f"{arguments[0]} failed with exit code {completed.returncode}; "
            f"see {stdout_path}"
        )
    return stdout_path.read_text(encoding="utf-8")


def require_package(package_directory: Path, package_id: str, version: str) -> None:
    package = package_directory / f"{package_id}.{version}.nupkg"
    if not package.is_file():
        raise ValueError(f"Candidate package is missing: {package.name}")


def verify_candidate_restore(
    package_directory: Path,
    package_cache: Path,
    version: str,
) -> None:
    for package_id in CANDIDATE_PACKAGE_IDS:
        if package_id == "NetWasm.Templates":
            continue
        candidate = package_directory / f"{package_id}.{version}.nupkg"
        restored_sha = (
            package_cache
            / package_id.lower()
            / version.lower()
            / f"{package_id.lower()}.{version.lower()}.nupkg.sha512"
        )
        if not restored_sha.is_file():
            raise RuntimeError(f"The restored graph omitted candidate package {package_id}.")
        expected = base64.b64encode(hashlib.sha512(candidate.read_bytes()).digest()).decode()
        actual = restored_sha.read_text(encoding="utf-8").strip()
        if actual != expected:
            raise RuntimeError(
                f"The restored {package_id} archive does not match the candidate feed."
            )


def write_proxy_project(root: Path) -> Path:
    project = root / "WasmOptRecorder.csproj"
    project.write_text(
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>wasm-opt-recorder</AssemblyName>
  </PropertyGroup>
</Project>
""",
        encoding="utf-8",
    )
    (root / "Program.cs").write_text(
        """using System.Diagnostics;
using System.Text.Json;

var realTool = Environment.GetEnvironmentVariable("NETWASM_REAL_WASM_OPT")
    ?? throw new InvalidOperationException("NETWASM_REAL_WASM_OPT is required.");
var tracePath = Environment.GetEnvironmentVariable("NETWASM_WASM_OPT_TRACE")
    ?? throw new InvalidOperationException("NETWASM_WASM_OPT_TRACE is required.");
File.AppendAllText(tracePath, JsonSerializer.Serialize(args) + Environment.NewLine);
var start = new ProcessStartInfo(realTool) { UseShellExecute = false };
foreach (var argument in args)
{
    start.ArgumentList.Add(argument);
}
using var process = Process.Start(start)
    ?? throw new InvalidOperationException("Could not start the real wasm-opt.");
process.WaitForExit();
return process.ExitCode;
""",
        encoding="utf-8",
    )
    return project


def parse_trace(path: Path, expected_flag: str | None) -> tuple[list[list[str]], list[str]]:
    if not path.exists():
        invocations: list[list[str]] = []
    else:
        invocations = [
            json.loads(line)
            for line in path.read_text(encoding="utf-8").splitlines()
            if line.strip()
        ]
    tool_invocations = [invocation for invocation in invocations if invocation != ["--version"]]
    expected_count = 0 if expected_flag is None else 2
    if len(tool_invocations) != expected_count:
        raise RuntimeError(
            f"Expected {expected_count} wasm-opt build invocations, found {len(tool_invocations)}."
        )
    phases: list[str] = []
    for invocation in tool_invocations:
        flags = [argument for argument in invocation if argument in OPTIMIZATION_FLAGS]
        if flags != [expected_flag]:
            raise RuntimeError(
                f"Expected exactly one {expected_flag} flag; recorded {flags}."
            )
        is_runtime = "--post-emscripten" in invocation
        is_final = (
            "--converge" in invocation
            and "--remove-unused-module-elements" in invocation
        )
        if is_runtime == is_final:
            raise RuntimeError("A wasm-opt invocation did not identify exactly one build phase.")
        phases.append("runtime" if is_runtime else "final")
    if phases != ([] if expected_flag is None else ["runtime", "final"]):
        raise RuntimeError(f"Expected runtime then final optimizer phases; recorded {phases}.")
    return tool_invocations, phases


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def qualify(args: argparse.Namespace) -> dict[str, object]:
    package_directory = args.package_directory.resolve(strict=True)
    host_tools_version = args.host_tools_version or args.version
    for package_id in CANDIDATE_PACKAGE_IDS:
        require_package(package_directory, package_id, args.version)

    retained = args.artifact_directory
    if retained is not None:
        retained = retained.expanduser().resolve()
        if retained.exists() or retained.is_symlink():
            raise ValueError("The artifact directory must not already exist.")
        retained.mkdir(parents=True)
        work_root = retained
        temporary = None
    else:
        temporary = tempfile.TemporaryDirectory(prefix="netwasm-optimization-modes.")
        work_root = Path(temporary.name)

    logs = work_root / "logs"
    app = work_root / "app"
    packages = work_root / "packages"
    dotnet_home = work_root / "dotnet-home"
    proxy_root = work_root / "proxy"
    proxy_output = work_root / "proxy-output"
    logs.mkdir()
    packages.mkdir()
    proxy_root.mkdir()

    environment = dict(os.environ)
    environment.update(
        {
            "DOTNET_CLI_HOME": str(dotnet_home),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
            "DOTNET_CLI_UI_LANGUAGE": "en-US",
            "NUGET_PACKAGES": str(packages),
        }
    )
    for name in (
        "EMSDK",
        "EMSDK_ROOT",
        "EMSDK_NODE",
        "NETWASM_EMSDK_ROOT",
        "NETWASM_NODE_PATH",
        "NETWASM_WASM_LD_PATH",
        "NETWASM_WASM_OPT_PATH",
        "NETWASM_WASM_MERGE_PATH",
    ):
        environment.pop(name, None)

    nuget_config = work_root / "NuGet.Config"
    nuget_config.write_text(
        "<configuration>\n"
        "  <packageSources>\n"
        "    <clear />\n"
        f'    <add key="candidate" value="{package_directory}" />\n'
        '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />\n'
        "  </packageSources>\n"
        "</configuration>\n",
        encoding="utf-8",
    )
    run(
        [
            "dotnet",
            "new",
            "install",
            str(package_directory / f"NetWasm.Templates.{args.version}.nupkg"),
            "--nuget-source",
            str(package_directory),
            "--force",
        ],
        environment=environment,
        cwd=work_root,
        stdout_path=logs / "template-install.log",
    )
    run(
        ["dotnet", "new", "netwasm-app", "-n", "NetWasmApp", "-o", str(app)],
        environment=environment,
        cwd=work_root,
        stdout_path=logs / "app-create.log",
    )
    project = app / "NetWasmApp.csproj"
    host_tools_property = f"-p:NetWasmHostToolsPackageVersion={host_tools_version}"
    run(
        [
            "dotnet",
            "restore",
            str(project),
            "--configfile",
            str(nuget_config),
            "--disable-build-servers",
            "--nologo",
            host_tools_property,
        ],
        environment=environment,
        cwd=app,
        stdout_path=logs / "initial-restore.log",
    )
    verify_candidate_restore(package_directory, packages, args.version)
    real_wasm_opt = run(
        [
            "dotnet",
            "msbuild",
            str(project),
            "-target:NetWasmSdkResolveBuildEnvironment",
            "-getProperty:NetWasmNativeBinaryenWasmOptPath",
            "-nologo",
            host_tools_property,
        ],
        environment=environment,
        cwd=app,
        stdout_path=logs / "resolve-wasm-opt.log",
    ).strip()
    if not real_wasm_opt or not Path(real_wasm_opt).is_file():
        raise RuntimeError("The candidate SDK did not resolve a packaged native wasm-opt.")

    proxy_project = write_proxy_project(proxy_root)
    run(
        [
            "dotnet",
            "publish",
            str(proxy_project),
            "-c",
            "Release",
            "--nologo",
            "--disable-build-servers",
            "-o",
            str(proxy_output),
        ],
        environment=environment,
        cwd=proxy_root,
        stdout_path=logs / "proxy-build.log",
    )
    proxy = proxy_output / ("wasm-opt-recorder.exe" if os.name == "nt" else "wasm-opt-recorder")
    if not proxy.is_file():
        raise RuntimeError("The wasm-opt recording proxy was not produced.")

    results: list[dict[str, object]] = []
    for mode in MODES:
        mode_root = work_root / "modes" / mode
        intermediate = mode_root / "obj"
        binaries = mode_root / "bin"
        published = mode_root / "publish"
        trace = mode_root / "wasm-opt.jsonl"
        mode_root.mkdir(parents=True)
        mode_environment = dict(environment)
        mode_environment.update(
            {
                "NETWASM_WASM_OPT_PATH": str(proxy),
                "NETWASM_REAL_WASM_OPT": real_wasm_opt,
                "NETWASM_WASM_OPT_TRACE": str(trace),
            }
        )
        properties = [
            f"-p:NetWasmOptimization={mode}",
            host_tools_property,
            f"-p:BaseIntermediateOutputPath={intermediate}{os.sep}",
            f"-p:BaseOutputPath={binaries}{os.sep}",
        ]
        run(
            [
                "dotnet",
                "restore",
                str(project),
                "--configfile",
                str(nuget_config),
                "--disable-build-servers",
                "--nologo",
                *properties,
            ],
            environment=mode_environment,
            cwd=app,
            stdout_path=logs / f"{mode}-restore.log",
        )
        run(
            [
                "dotnet",
                "publish",
                str(project),
                "-c",
                "Release",
                "--no-restore",
                "--disable-build-servers",
                "--nologo",
                "-p:NetWasmPublishTarget=portable",
                "-o",
                str(published),
                *properties,
            ],
            environment=mode_environment,
            cwd=app,
            stdout_path=logs / f"{mode}-publish.log",
        )
        component = published / "NetWasmApp.wasm"
        if not component.is_file():
            raise RuntimeError(f"The {mode} publish did not produce NetWasmApp.wasm.")
        invocations, phases = parse_trace(trace, CANONICAL_FLAGS[mode])
        results.append(
            {
                "mode": mode,
                "canonicalMode": mode,
                "optimizationFlag": CANONICAL_FLAGS[mode],
                "optimizerInvocations": len(invocations),
                "optimizerPhases": phases,
                "componentBytes": component.stat().st_size,
                "componentSha256": sha256(component),
            }
        )

    receipt: dict[str, object] = {
        "schemaVersion": 1,
        "packageVersion": args.version,
        "hostToolsVersion": host_tools_version,
        "modes": results,
    }
    if args.receipt is not None:
        receipt_path = args.receipt.expanduser().resolve()
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    if temporary is not None:
        temporary.cleanup()
    return receipt


def main() -> int:
    try:
        qualify(parse_arguments())
        return 0
    except (OSError, RuntimeError, ValueError, subprocess.SubprocessError) as error:
        print(f"Optimization-mode qualification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
