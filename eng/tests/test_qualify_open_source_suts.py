#!/usr/bin/env python3
# Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
# SPDX-License-Identifier: MIT

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT_PATH = Path(__file__).parents[1] / "qualify-open-source-suts.py"
SPEC = importlib.util.spec_from_file_location("qualify_open_source_suts", SCRIPT_PATH)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class PinTests(unittest.TestCase):
    def test_reads_one_exact_version_per_line(self) -> None:
        self.assertEqual([("Sep", "0.18.0"), ("Microsoft.Extensions.Hosting", "11.0.0"), ("Pre.Release", "1.2.3-beta.4")],
                         MODULE.pins("Sep 0.18.0\n\nMicrosoft.Extensions.Hosting 11.0.0\nPre.Release 1.2.3-beta.4\n"))

    def test_rejects_ranges_floating_versions_and_missing_versions(self) -> None:
        for line in ("Sep [0.18.0,)", "Sep 0.18.*", "Sep", "Sep 0.18"):
            with self.subTest(line=line), self.assertRaisesRegex(ValueError, "not an exact package pin"):
                MODULE.pins(line)

    def test_each_side_adds_its_own_pins_to_the_shared_ones(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            system = Path(directory)
            (system / "packages.txt").write_text("Shared 1.0.0\n")
            (system / "packages.netwasm.txt").write_text("NetWasm.Port 0.15.0\n")
            self.assertEqual([("Shared", "1.0.0"), ("NetWasm.Port", "0.15.0")], MODULE.system_pins(system, netwasm=True))
            self.assertEqual([("Shared", "1.0.0")], MODULE.system_pins(system, netwasm=False))

    def test_reads_one_property_assignment_per_line(self) -> None:
        self.assertEqual([("NetWasmMissingApiAsError", "true"), ("Empty", ""), ("With-Hyphen", "a=b")],
                         MODULE.properties("NetWasmMissingApiAsError=true\n\nEmpty=\nWith-Hyphen=a=b\n"))

    def test_rejects_lines_that_assign_no_property(self) -> None:
        for line in ("NetWasmMissingApiAsError", "=true", "1Name=true", "Name true"):
            with self.subTest(line=line), self.assertRaisesRegex(ValueError, "not an MSBuild property assignment"):
                MODULE.properties(line)

    def test_only_the_netwasm_build_takes_the_system_properties(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            system = Path(directory)
            self.assertEqual([], MODULE.system_properties(system, netwasm=True))
            (system / "properties.netwasm.txt").write_text("NetWasmMissingApiAsError=true\n")
            self.assertEqual([("NetWasmMissingApiAsError", "true")], MODULE.system_properties(system, netwasm=True))
            self.assertEqual([], MODULE.system_properties(system, netwasm=False))

    def test_every_system_has_a_program_and_exact_pins(self) -> None:
        systems = sorted(path for path in MODULE.SYSTEMS.iterdir() if path.is_dir())
        self.assertTrue(systems)
        for system in systems:
            with self.subTest(system=system.name):
                self.assertTrue((system / "Program.cs").is_file())
                MODULE.system_pins(system, netwasm=True)
                MODULE.system_pins(system, netwasm=False)


class ProjectTests(unittest.TestCase):
    def test_netwasm_and_desktop_projects_differ_only_in_sdk_and_framework(self) -> None:
        packages = [("Sep", "0.18.0")]
        netwasm = MODULE.project("sep", packages, netwasm=True)
        desktop = MODULE.project("sep", packages, netwasm=False)
        self.assertIn('<Project Sdk="NetWasm.Sdk">', netwasm)
        self.assertIn("<TargetFramework>net10.0-netwasm</TargetFramework>", netwasm)
        self.assertIn('<Project Sdk="Microsoft.NET.Sdk">', desktop)
        self.assertIn("<TargetFramework>net11.0</TargetFramework>", desktop)
        for text in (netwasm, desktop):
            self.assertIn('<PackageReference Include="Sep" Version="0.18.0" />', text)
            self.assertIn("<AssemblyName>OpenSourceSut.sep</AssemblyName>", text)
            self.assertIn("<InvariantGlobalization>true</InvariantGlobalization>", text)
            self.assertIn("<NuGetAudit>false</NuGetAudit>", text)

    def test_the_system_properties_join_the_property_group_escaped(self) -> None:
        text = MODULE.project("sep", [], netwasm=True, build_properties=[("NetWasmMissingApiAsError", "true"), ("Note", "a<b&c")])
        self.assertIn("    <NetWasmMissingApiAsError>true</NetWasmMissingApiAsError>\n", text)
        self.assertIn("    <Note>a&lt;b&amp;c</Note>\n", text)
        self.assertLess(text.index("<NetWasmMissingApiAsError>"), text.index("</PropertyGroup>"))

    def test_a_system_without_packages_has_no_item_group(self) -> None:
        self.assertNotIn("<ItemGroup>", MODULE.project("stack", [], netwasm=True))

    def test_wasm64_is_raw_wasm(self) -> None:
        self.assertEqual(["-p:NetWasmTarget=wasm32"], MODULE.target_properties("wasm32"))
        self.assertEqual(["-p:NetWasmTarget=wasm64", "-p:NetWasmRawWasm=true"], MODULE.target_properties("wasm64"))


class ConfigureTests(unittest.TestCase):
    def test_selects_the_candidate_and_stops_the_directory_build_search(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            MODULE.configure(root, Path("/packages"), "1.2.3-ci.4")
            self.assertEqual({"NetWasm.Sdk": "1.2.3-ci.4"}, json.loads((root / "global.json").read_text())["msbuild-sdks"])
            config = (root / "NuGet.Config").read_text()
            self.assertIn("<clear />", config)
            self.assertIn('value="/packages"', config)
            for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
                self.assertEqual("<Project />\n", (root / name).read_text())

    def test_stage_copies_the_sources_beside_a_fresh_project(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            system = Path(directory, "demo")
            system.mkdir()
            (system / "Program.cs").write_text("System.Console.WriteLine(1);\n")
            (system / "packages.txt").write_text("Sep 0.18.0\n")
            (system / "properties.netwasm.txt").write_text("NetWasmMissingApiAsError=true\n")
            target = Path(directory, "work", "demo", "wasm32")
            target.mkdir(parents=True)
            (target / "stale.cs").write_text("")
            project = MODULE.stage(system, target, netwasm=True)
            self.assertEqual(["Program.cs", "demo.csproj"], sorted(path.name for path in target.iterdir()))
            self.assertIn("net10.0-netwasm", project.read_text())
            self.assertIn("<NetWasmMissingApiAsError>true</NetWasmMissingApiAsError>", project.read_text())
            desktop = MODULE.stage(system, Path(directory, "work", "demo", "desktop"), netwasm=False)
            self.assertNotIn("NetWasmMissingApiAsError", desktop.read_text())


class DifferenceTests(unittest.TestCase):
    def test_the_portable_desktop_run_turns_hardware_intrinsics_off(self) -> None:
        environment = {"PATH": "/bin"}
        self.assertEqual({"PATH": "/bin", "DOTNET_EnableHWIntrinsic": "0"}, MODULE.portable(environment))
        self.assertEqual({"PATH": "/bin"}, environment)

    def test_a_run_matches_desktop_with_or_without_hardware_intrinsics(self) -> None:
        # On x86, ImageSharp's WebP lossy encoder writes 760 bytes with its SSE paths and 756 with its portable
        # ones, which NetWasm and desktop .NET on Arm also write.
        intrinsic, portable = MODULE.Run(0, "webp 760\n", ""), MODULE.Run(0, "webp 756\n", "")
        self.assertIsNone(MODULE.outcome(MODULE.Run(0, "webp 756\n", ""), [intrinsic, portable]))
        self.assertIsNone(MODULE.outcome(MODULE.Run(0, "webp 760\n", ""), [intrinsic, portable]))
        self.assertEqual("line 1: desktop 'webp 760', NetWasm 'webp 755'",
                         MODULE.outcome(MODULE.Run(0, "webp 755\n", ""), [intrinsic, portable]))
        self.assertEqual("exit 1, desktop 0; line 1: desktop 'webp 760', NetWasm 'webp 756'\ntrace",
                         MODULE.outcome(MODULE.Run(1, "webp 756\n", "trace"), [intrinsic, portable]))

    def test_names_the_first_line_that_differs(self) -> None:
        self.assertEqual("line 2: desktop 'b', NetWasm 'c'", MODULE.difference("a\nb\nx\n", "a\nc\ny\n"))

    def test_names_a_missing_tail(self) -> None:
        self.assertEqual("line 2: desktop has 2 lines, NetWasm 1", MODULE.difference("a\nb\n", "a\n"))

    def test_collects_distinct_build_errors(self) -> None:
        log = "x.cs(1,1): error CS1: one\nx.cs(1,1): error CS1: one\nwarning\n"
        self.assertEqual("x.cs(1,1): error CS1: one", MODULE.build_errors(log))


if __name__ == "__main__":
    unittest.main()
