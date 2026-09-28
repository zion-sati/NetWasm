import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "classify-ci-impact.py"
SPEC = importlib.util.spec_from_file_location("classify_ci_impact", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
IMPACT = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = IMPACT
SPEC.loader.exec_module(IMPACT)


class CiImpactTests(unittest.TestCase):
    def test_markdown_add_edit_and_delete_are_documentation_only(self):
        changes = [
            IMPACT.Change("README.md", b"old", b"new"),
            IMPACT.Change("docs/new.md", None, b"new"),
            IMPACT.Change("docs/old.md", b"old", None),
        ]

        self.assertEqual("docs", IMPACT.classify(changes))

    def test_package_description_only_is_documentation(self):
        before = b"<Project><PropertyGroup><Version>1</Version><Description>Before</Description></PropertyGroup></Project>"
        after = b"<Project><PropertyGroup><Version>1</Version><Description>After</Description></PropertyGroup></Project>"

        self.assertEqual(
            "docs",
            IMPACT.classify([IMPACT.Change("src/Example/Example.csproj", before, after)]),
        )

    def test_project_semantics_cannot_hide_behind_description_change(self):
        before = b"<Project><PropertyGroup><Version>1</Version><Description>Before</Description></PropertyGroup></Project>"
        after = b"<Project><PropertyGroup><Version>2</Version><Description>After</Description></PropertyGroup></Project>"

        self.assertEqual(
            "full",
            IMPACT.classify([IMPACT.Change("src/Example/Example.csproj", before, after)]),
        )

    def test_code_workflow_non_xml_and_symlink_changes_are_full(self):
        changes = [
            IMPACT.Change("src/Compiler.cs", b"old", b"new"),
            IMPACT.Change(".github/workflows/ci.yml", b"old", b"new"),
            IMPACT.Change("src/Broken.csproj", b"not xml", b"still not xml"),
            IMPACT.Change("docs/link.md", b"old", b"target", current_mode="120000"),
        ]

        for change in changes:
            with self.subTest(path=change.path):
                self.assertEqual("full", IMPACT.classify([change]))

    def test_empty_change_set_fails_closed(self):
        self.assertEqual("full", IMPACT.classify([]))

    def test_changed_markdown_links_must_resolve(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "docs").mkdir()
            (root / "docs/index.md").write_text("[Good](page.md)\n")
            (root / "docs/page.md").write_text("ok\n")
            change = IMPACT.Change(
                "docs/index.md",
                b"old",
                (root / "docs/index.md").read_bytes(),
            )
            IMPACT.verify_changed_markdown_links(root, [change])
            broken = IMPACT.Change("docs/index.md", b"old", b"[Bad](missing.md)\n")
            with self.assertRaisesRegex(ValueError, "broken local Markdown link"):
                IMPACT.verify_changed_markdown_links(root, [broken])


if __name__ == "__main__":
    unittest.main()
