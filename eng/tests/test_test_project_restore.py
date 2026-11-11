import re
import subprocess
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).parents[2]


def xunit_test_projects() -> list[Path]:
    return sorted(
        project
        for project in (REPOSITORY_ROOT / "tests").glob("*/*.csproj")
        if re.search(r'PackageReference Include="xunit\.v3"', project.read_text())
    )


def restore_output_type(project: Path) -> str:
    # Restore evaluates a project without the imports of its packages.
    result = subprocess.run(
        ["dotnet", "msbuild", str(project), "-p:ExcludeRestorePackageImports=true",
         "-getProperty:OutputType"],
        capture_output=True, text=True, check=True, cwd=REPOSITORY_ROOT,
    )
    return result.stdout.strip()


class TestProjectRestoreTests(unittest.TestCase):
    def test_restore_sees_every_xunit_test_project_as_an_executable(self) -> None:
        # Restore downloads the app host pack only for an executable; a test project that only
        # its packages make executable then builds without an app host on a clean machine.
        projects = xunit_test_projects()
        self.assertGreater(len(projects), 20)
        with ThreadPoolExecutor(max_workers=8) as pool:
            output_types = dict(zip(projects, pool.map(restore_output_type, projects)))
        for project, output_type in output_types.items():
            with self.subTest(project=project.name):
                self.assertEqual("Exe", output_type)


if __name__ == "__main__":
    unittest.main()
