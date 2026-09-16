import importlib.util
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("prepare_release", Path(__file__).resolve().parents[1] / "prepare-release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class PrepareReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "eng").mkdir()
        self.manifest = {"schemaVersion": 1, "repository": "zion-sati/NetWasm",
                         "repositoryUrl": "https://github.com/zion-sati/netwasm",
                         "releaseVersion": "0.1.0-rc.1", "releaseTag": "v0.1.0-rc.1",
                         "sourceCommit": "previous", "packages": ["NetWasm.Sdk"]}
        self.write_manifest()
        (self.root / "eng/release-signers").write_text("public@example.com ssh-ed25519 PUBLIC_KEY\n")
        (self.root / "eng/NetWasm.ReleaseVersion.txt").write_text("0.1.0-rc.1\n")
        self.calls = []
        self.responses = {("branch", "--show-current"): "main", ("status", "--porcelain=v1"): "",
                          ("remote", "get-url", "origin"): "https://github.com/zion-sati/NetWasm.git",
                          ("config", "user.name"): "Zion Sati", ("config", "user.email"): "public@example.com"}
        self.revisions = iter(["signed-source", "signed-manifest"])

    def write_manifest(self):
        (self.root / "eng/release-manifest.json").write_text(json.dumps(self.manifest))

    def git(self, root, *arguments):
        self.assertEqual(self.root.resolve(), root)
        self.calls.append(arguments)
        if arguments == ("rev-parse", "HEAD"):
            return next(self.revisions)
        return self.responses.get(arguments, "")

    def run_prepare(self, version="0.1.0-rc.2"):
        with patch.object(release, "git", side_effect=self.git), \
             patch.object(release.projection, "project_version") as project, \
             patch.object(release.verification, "verify_source") as verify:
            result = release.prepare(self.root, version)
            project.assert_called_once()
            verify.assert_called_once_with(self.root.resolve(),
                json.loads((self.root / "eng/release-manifest.json").read_text()), self.root.resolve() / "eng/release-signers")
        return result

    def test_prepares_signed_source_tag_and_matching_manifest_without_push_or_build(self):
        result = self.run_prepare()
        self.assertEqual({"releaseVersion": "0.1.0-rc.2", "releaseTag": "v0.1.0-rc.2",
                          "sourceCommit": "signed-source", "manifestCommit": "signed-manifest"}, result)
        self.assertEqual(2, sum(call[0] == "commit" and "-S" in call for call in self.calls))
        self.assertIn(("tag", "-s", "v0.1.0-rc.2", "-m", "NetWasm 0.1.0-rc.2", "signed-source"), self.calls)
        self.assertFalse(any(call[0] in {"push", "build", "test", "release"} for call in self.calls))
        manifest = json.loads((self.root / "eng/release-manifest.json").read_text())
        self.assertEqual(["NetWasm.Sdk"], manifest["packages"])

    def test_preserves_tunit_tag_prefix(self):
        self.manifest.update(repository="zion-sati/TUnit-NetWasm", releaseTag="netwasm-v0.1.0-rc.1")
        self.write_manifest()
        self.responses[("remote", "get-url", "origin")] = "git@github.com:zion-sati/TUnit-NetWasm.git"
        self.assertEqual("netwasm-v0.1.0-rc.2", self.run_prepare()["releaseTag"])

    def test_preflight_rejects_dirty_branch_private_origin_or_unapproved_identity_before_mutation(self):
        cases = [(("branch", "--show-current"), "feature"), (("status", "--porcelain=v1"), " M code"),
                 (("remote", "get-url", "origin"), "https://github.com/private/project.git"),
                 (("config", "user.name"), "Private Author"), (("config", "user.email"), "private@example.com"),
                 (("tag", "--list", "v0.1.0-rc.2"), "v0.1.0-rc.2"),
                 (("ls-remote", "--tags", "origin", "refs/tags/v0.1.0-rc.2"), "existing")]
        for key, value in cases:
            with self.subTest(key=key), patch.object(release, "git", side_effect=self.git), \
                 patch.object(release.projection, "project_version") as project:
                previous = self.responses.get(key, "")
                self.responses[key] = value
                self.calls.clear()
                with self.assertRaises(ValueError):
                    release.prepare(self.root, "0.1.0-rc.2")
                project.assert_not_called()
                self.assertFalse(any(call[0] in {"add", "commit", "push"} for call in self.calls))
                self.responses[key] = previous

    def test_rejects_invalid_or_already_prepared_version(self):
        for version in ("not a version", "0.1.0-rc.1"):
            with self.subTest(version=version), patch.object(release, "git", side_effect=self.git), \
                 patch.object(release.projection, "project_version") as project:
                with self.assertRaises(ValueError):
                    release.prepare(self.root, version)
                project.assert_not_called()

    def test_rejects_inconsistent_tag_before_mutation(self):
        self.manifest["releaseTag"] = "different"
        self.write_manifest()
        with patch.object(release, "git", side_effect=self.git), \
             patch.object(release.projection, "project_version") as project:
            with self.assertRaises(ValueError):
                release.prepare(self.root, "0.1.0-rc.2")
            project.assert_not_called()

    def test_rejects_inconsistent_source_version_before_mutation(self):
        (self.root / "eng/NetWasm.ReleaseVersion.txt").write_text("different\n")
        with patch.object(release, "git", side_effect=self.git), \
             patch.object(release.projection, "project_version") as project:
            with self.assertRaises(ValueError):
                release.prepare(self.root, "0.1.0-rc.2")
            project.assert_not_called()


class PrepareReleaseGitIntegrationTests(unittest.TestCase):
    def test_real_git_signed_commits_tag_and_version_projection(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "repository"
            root.mkdir()
            (root / "eng").mkdir()
            key = Path(temporary) / "signing"
            subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)],
                           capture_output=True, check=True)
            (root / "eng/release-signers").write_text("public@example.com " + key.with_suffix(".pub").read_text())
            (root / "eng/NetWasm.ReleaseVersion.txt").write_text("0.1.0-rc.1\n")
            (root / "global.json").write_text(json.dumps({"msbuild-sdks": {"NetWasm.Sdk": "0.1.0-rc.1"}}))
            manifest = {"schemaVersion": 1, "repository": "zion-sati/NetWasm",
                        "repositoryUrl": "https://github.com/zion-sati/netwasm",
                        "releaseVersion": "0.1.0-rc.1", "releaseTag": "v0.1.0-rc.1",
                        "sourceCommit": "initial", "packages": ["NetWasm.Sdk"]}
            (root / "eng/release-manifest.json").write_text(json.dumps(manifest))
            actual_git = release.git
            actual_git(root, "init", "-b", "main")
            for option, value in (("user.name", "Zion Sati"), ("user.email", "public@example.com"),
                                  ("gpg.format", "ssh"), ("user.signingkey", str(key)),
                                  ("gpg.ssh.allowedSignersFile", str(root / "eng/release-signers"))):
                actual_git(root, "config", option, value)
            actual_git(root, "remote", "add", "origin", "https://github.com/zion-sati/NetWasm.git")
            actual_git(root, "add", ".")
            actual_git(root, "commit", "-m", "Initial release tooling fixture")

            def local_git(repository, *arguments):
                return "" if arguments[0] == "ls-remote" else actual_git(repository, *arguments)

            with patch.object(release, "git", side_effect=local_git):
                receipt = release.prepare(root, "0.1.0-rc.2")
            self.assertEqual("", actual_git(root, "status", "--porcelain=v1"))
            self.assertEqual(receipt["sourceCommit"], actual_git(root, "rev-parse", "v0.1.0-rc.2^{commit}"))
            self.assertEqual(receipt["manifestCommit"], actual_git(root, "rev-parse", "HEAD"))
            self.assertEqual(["G", "G"], actual_git(root, "log", "-2", "--format=%G?").splitlines())
            actual_git(root, "verify-tag", "v0.1.0-rc.2")
            self.assertEqual("0.1.0-rc.2", actual_git(root, "show", f"{receipt['sourceCommit']}:eng/NetWasm.ReleaseVersion.txt"))
            self.assertEqual(receipt["sourceCommit"], json.loads((root / "eng/release-manifest.json").read_text())["sourceCommit"])


if __name__ == "__main__":
    unittest.main()
