"""Tests for the workflow check (CI-03, CI-04).

    python3 .github/scripts/tests/test_check_actions.py
"""

import importlib.util
import pathlib
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

SCRIPTS = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("check_actions", SCRIPTS / "check-actions.py")
check_actions = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check_actions)

SHA = "3d3c42e5aac5ba805825da76410c181273ba90b1"
CI = f"""name: ci
on: [pull_request]
permissions:
  contents: read
jobs:
  build:
    name: build and test
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@{SHA} # v7.0.1
  required:
    name: ci passed
    needs: build
    runs-on: ubuntu-24.04
    steps:
      - run: test "${{{{ needs.build.result }}}}" = success
"""
SCAN = f"""name: secret-scan
on: [pull_request]
jobs:
  gitleaks:
    name: secret scan
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@{SHA}
"""


class CheckActions(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.root = pathlib.Path(self.folder.name)
        self.write("actions.allow", f"actions/checkout@{SHA}\n")
        self.write("ci.yml", CI)
        self.write("secret-scan.yml", SCAN)

    def tearDown(self):
        self.folder.cleanup()

    def write(self, name, text):
        path = self.root / ".github" / "workflows" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def problems(self):
        return check_actions.check(self.root)[0]

    def assertRefused(self, fragment):
        problems = self.problems()
        self.assertTrue(any(fragment in p for p in problems), f"expected {fragment!r} in {problems}")

    def test_the_reviewed_workflows_pass(self):
        self.assertEqual(self.problems(), [])

    def test_a_new_workflow_file_is_refused(self):
        self.write("extra.yaml", "name: extra\non: [push]\njobs: {}\n")
        self.assertRefused("extra.yaml: not a reviewed workflow")

    def test_another_job_named_like_a_required_check_is_refused(self):
        for name in ("ci passed", "'ci passed'", '"Secret Scan"'):
            with self.subTest(name=name):
                self.write("ci.yml", CI + f"  impostor:\n    name: {name}\n    runs-on: ubuntu-24.04\n    steps:\n      - run: true\n")
                self.assertRefused("must be named exactly once")
        self.write("ci.yml", CI)
        self.write("secret-scan.yml", SCAN.replace("name: secret scan", "name: ci passed"))
        self.assertRefused("'secret scan' must be named exactly once")

    def test_unpinned_or_unlisted_actions_are_refused_in_block_and_flow_style(self):
        cases = {
            "      - uses: actions/checkout@v7\n": "not pinned to a full commit SHA",
            "      - { uses: actions/setup-node@v7, with: {} }\n": "not pinned to a full commit SHA",
            f"      - {{\"uses\": \"actions/cache@{'a' * 40}\"}}\n": "not in actions.allow",
            "      - uses: ./.github/actions/local\n": "local or container action",
            "      - uses: docker://alpine:3\n": "local or container action",
        }
        for line, fragment in cases.items():
            with self.subTest(line=line):
                self.write("ci.yml", CI.replace("      - run: test", line + "      - run: test"))
                self.assertRefused(fragment)


if __name__ == "__main__":
    unittest.main(verbosity=1)
