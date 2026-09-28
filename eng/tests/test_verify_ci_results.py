import importlib.util
from pathlib import Path
import sys
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "verify-ci-results.py"
SPEC = importlib.util.spec_from_file_location("verify_ci_results", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
RESULTS = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = RESULTS
SPEC.loader.exec_module(RESULTS)


class CiResultTests(unittest.TestCase):
    def test_documentation_scope_accepts_only_documentation_gate(self):
        RESULTS.verify("docs", {
            "impact": "success",
            "docs": "success",
            "ci-infrastructure": "skipped",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        })

    def test_ci_scope_accepts_only_ci_infrastructure_gate(self):
        RESULTS.verify("ci", {
            "impact": "success",
            "docs": "skipped",
            "ci-infrastructure": "success",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        })

    def test_full_scope_accepts_every_heavy_gate(self):
        RESULTS.verify("full", {
            "impact": "success",
            "docs": "skipped",
            "ci-infrastructure": "skipped",
            "test": "success",
            "timings": "success",
            "package": "success",
            "host-tools": "success",
            "verify-packages": "success",
            "runtime-pack-hosts": "success",
        })

    def test_reuse_scope_accepts_only_the_qualification_gate(self):
        RESULTS.verify("reuse", {
            "impact": "success",
            "docs": "skipped",
            "ci-infrastructure": "skipped",
            "test": "skipped",
            "timings": "skipped",
            "package": "skipped",
            "host-tools": "skipped",
            "verify-packages": "skipped",
            "runtime-pack-hosts": "skipped",
        })

    def test_failed_impact_unknown_scope_and_missing_gate_fail(self):
        with self.assertRaisesRegex(ValueError, "impact classification failed"):
            RESULTS.verify("full", {"impact": "failure"})
        with self.assertRaisesRegex(ValueError, "unsupported CI impact scope"):
            RESULTS.verify("unknown", {"impact": "success"})
        with self.assertRaisesRegex(ValueError, "runtime-pack-hosts"):
            RESULTS.verify("full", {
                "impact": "success",
                "docs": "skipped",
                "ci-infrastructure": "skipped",
                "test": "success",
                "timings": "success",
                "package": "success",
                "host-tools": "success",
                "verify-packages": "success",
                "runtime-pack-hosts": "failure",
            })


if __name__ == "__main__":
    unittest.main()
