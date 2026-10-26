"""Tests for the supply-chain check and the dependency register (SEC-02).

    python3 .github/scripts/tests/test_supply_chain.py

Each test builds a small repository in a temporary folder and shows one rule
holding or one violation being refused.
"""

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # keep the scripts folder free of __pycache__

SCRIPTS = pathlib.Path(__file__).resolve().parent.parent


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


supply_chain = load("check-supply-chain")
register = load("dependency-register")

NUGET_CONFIG = """<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"""
PACKAGES_PROPS = """<Project>
  <PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
  <ItemGroup><PackageVersion Include="Example.Package" Version="1.2.3" /></ItemGroup>
</Project>
"""
BUILD_PROPS = """<Project>
  <PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>
</Project>
"""
PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup><PackageReference Include="Example.Package" /></ItemGroup>
</Project>
"""
LOCK = {"version": 1, "dependencies": {"net10.0": {
    "Example.Package": {"type": "Direct", "requested": "[1.2.3, )", "resolved": "1.2.3", "contentHash": "abc=="},
    "Example.Dependency": {"type": "Transitive", "resolved": "4.5.6", "contentHash": "def=="}}}}
WORKFLOW = """name: ci
on: [pull_request]
jobs:
  build:
    runs-on: ubuntu-24.04
    steps:
      - run: dotnet restore --locked-mode
"""


class Repository:
    """A minimal repository that keeps every supply-chain rule, committed to git."""

    def __init__(self, folder):
        self.root = pathlib.Path(folder)
        self.write("NuGet.Config", NUGET_CONFIG)
        self.write("Directory.Packages.props", PACKAGES_PROPS)
        self.write("Directory.Build.props", BUILD_PROPS)
        self.write(".npmrc", "ignore-scripts=true\n")
        self.write("tests/Example.Tests/Example.Tests.csproj", PROJECT)
        self.write("tests/Example.Tests/packages.lock.json", json.dumps(LOCK))
        self.write(".github/workflows/ci.yml", WORKFLOW)
        self.git("init", "-q")
        self.commit()

    def write(self, path, text):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.root), "-c", "user.name=test", "-c", "user.email=test@example.invalid",
                        "-c", "commit.gpgsign=false", *args], check=True, capture_output=True)

    def commit(self):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "state", "--allow-empty")

    def problems(self):
        return supply_chain.check(self.root)


class SupplyChainCheck(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.repo = Repository(self.folder.name)

    def tearDown(self):
        self.folder.cleanup()

    def assertRefused(self, fragment):
        problems = self.repo.problems()
        self.assertTrue(any(fragment in p for p in problems), f"expected a problem mentioning {fragment!r}, got {problems}")

    def test_a_compliant_repository_passes(self):
        self.assertEqual(self.repo.problems(), [])

    def test_a_project_without_a_lock_file_is_refused(self):
        (self.repo.root / "tests/Example.Tests/packages.lock.json").unlink()
        self.assertRefused("has no packages.lock.json")

    def test_an_uncommitted_lock_file_is_refused(self):
        self.repo.write("src/Example/Example.csproj", PROJECT)
        self.repo.git("add", "src/Example/Example.csproj")
        self.repo.git("commit", "-q", "-m", "project only")
        self.repo.write("src/Example/packages.lock.json", json.dumps(LOCK))
        self.assertRefused("src/Example/packages.lock.json: exists but is not committed")

    def test_a_version_on_a_package_reference_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj",
                        PROJECT.replace('Include="Example.Package"', 'Include="Example.Package" Version="1.2.3"'))
        self.assertRefused("sets Version")

    def test_a_version_override_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj",
                        PROJECT.replace('Include="Example.Package"', 'Include="Example.Package" VersionOverride="1.2.4"'))
        self.assertRefused("sets VersionOverride")

    def test_a_floating_central_version_is_refused(self):
        self.repo.write("Directory.Packages.props", PACKAGES_PROPS.replace('Version="1.2.3"', 'Version="1.*"'))
        self.assertRefused("must have an exact version")

    def test_lock_files_turned_off_are_refused(self):
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace(">true<", ">false<"))
        self.assertRefused("RestorePackagesWithLockFile")

    def test_a_second_package_source_is_refused(self):
        self.repo.write("NuGet.Config", NUGET_CONFIG.replace(
            '<add key="nuget.org"', '<add key="other" value="https://example.invalid/v3/index.json" />\n    <add key="nuget.org"'))
        self.assertRefused("the only package source must be")

    def test_inherited_sources_are_refused(self):
        self.repo.write("NuGet.Config", NUGET_CONFIG.replace("    <clear />\n", ""))
        self.assertRefused("must start with <clear />")

    def test_unmapped_packages_are_refused(self):
        self.repo.write("NuGet.Config", NUGET_CONFIG.replace('pattern="*"', 'pattern="Example.*"'))
        self.assertRefused("packageSourceMapping")

    def test_a_restore_source_property_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace(
            "<ItemGroup>", "<PropertyGroup><RestoreSources>https://example.invalid/v3/index.json</RestoreSources></PropertyGroup>\n  <ItemGroup>"))
        self.assertRefused("sets RestoreSources")

    def test_an_additional_source_in_a_props_file_is_refused(self):
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace(
            "</PropertyGroup>", "<RestoreAdditionalProjectSources>https://example.invalid/v3/index.json</RestoreAdditionalProjectSources></PropertyGroup>"))
        self.assertRefused("sets RestoreAdditionalProjectSources")

    def test_a_package_reference_in_a_targets_file_is_refused(self):
        self.repo.write("Directory.Build.targets",
                        '<Project><ItemGroup><PackageReference Include="Example.Package" Version="9.9.9" /></ItemGroup></Project>')
        self.assertRefused("declares PackageReference Example.Package")

    def test_a_source_on_the_command_line_in_a_workflow_is_refused(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - run: dotnet restore --locked-mode --source https://example.invalid/v3/index.json\n")
        self.assertRefused("never from the command line")

    def test_adding_a_source_in_a_workflow_is_refused(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - run: dotnet nuget add source https://example.invalid/v3/index.json\n")
        self.assertRefused("workflows never add package sources")

    def test_a_nested_nuget_config_is_refused(self):
        self.repo.write("src/nuget.config", NUGET_CONFIG)
        self.assertRefused("only the root NuGet.Config")

    def test_npm_install_scripts_are_refused(self):
        self.repo.write(".npmrc", "ignore-scripts=false\n")
        self.assertRefused("ignore-scripts=true")

    def test_a_package_json_without_a_lock_is_refused(self):
        self.repo.write("src/surface/package.json", "{}")
        self.assertRefused("has no package-lock.json")

    def test_an_unlocked_restore_in_a_workflow_is_refused(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW.replace(" --locked-mode", ""))
        self.assertRefused("dotnet restore must use --locked-mode")

    def test_npm_install_in_a_workflow_is_refused(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - run: npm install\n")
        self.assertRefused("never npm install")

    def test_npm_ci_with_scripts_in_a_workflow_is_refused(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - run: npm ci\n")
        self.assertRefused("npm ci must use --ignore-scripts")


class DependencyRegister(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        base = pathlib.Path(self.folder.name)
        self.repo = Repository(base / "repository")
        self.packages = base / "packages"
        self.add_package("Example.Package", "1.2.3", "MIT")
        self.add_package("Example.Dependency", "4.5.6", "Apache-2.0")
        self.repo.write("docs/dependencies/license-policy.json",
                        json.dumps({"allowed": ["MIT", "Apache-2.0"], "exceptions": []}))
        self.environment = mock_environment(NUGET_PACKAGES=str(self.packages))
        self.environment.__enter__()

    def tearDown(self):
        self.environment.__exit__(None, None, None)
        self.folder.cleanup()

    def add_package(self, name, version, license, source=register.NUGET_ORG):
        folder = self.packages / name.lower() / version
        folder.mkdir(parents=True, exist_ok=True)
        (folder / f"{name.lower()}.nuspec").write_text(
            f'<package><metadata><id>{name}</id><license type="expression">{license}</license></metadata></package>',
            encoding="utf-8")
        (folder / ".nupkg.metadata").write_text(json.dumps({"version": 2, "source": source}), encoding="utf-8")

    def run_script(self, command):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = register.main(["dependency-register.py", command, str(self.repo.root)])
        return code, output.getvalue()

    def test_a_written_register_checks_clean(self):
        self.assertEqual(self.run_script("write")[0], 0)
        code, output = self.run_script("check")
        self.assertEqual(code, 0, output)
        written = json.loads((self.repo.root / "docs/dependencies/register.json").read_text())
        self.assertEqual([p["id"] for p in written["packages"]], ["Example.Dependency", "Example.Package"])
        self.assertEqual({p["ecosystem"] for p in written["packages"]}, {"nuget"})
        self.assertEqual(written["packages"][1]["projects"], [{"path": "tests/Example.Tests", "dependency": "direct"}])
        self.assertEqual(written["packages"][1]["uses"], ["test"])

    def test_a_changed_lock_file_makes_the_register_out_of_date(self):
        self.run_script("write")
        changed = json.loads(json.dumps(LOCK))
        changed["dependencies"]["net10.0"]["Example.Dependency"]["resolved"] = "4.5.7"
        self.add_package("Example.Dependency", "4.5.7", "Apache-2.0")
        self.repo.write("tests/Example.Tests/packages.lock.json", json.dumps(changed))
        code, output = self.run_script("check")
        self.assertEqual(code, 1)
        self.assertIn("added: Example.Dependency 4.5.7", output)
        self.assertIn("removed: Example.Dependency 4.5.6", output)

    def test_a_disallowed_license_is_refused(self):
        self.add_package("Example.Dependency", "4.5.6", "GPL-3.0-only")
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("is not allowed for test use", output)

    def test_a_recorded_exception_allows_a_license(self):
        self.add_package("Example.Dependency", "4.5.6", "MPL-2.0")
        self.repo.write("docs/dependencies/license-policy.json", json.dumps({"allowed": ["MIT", "Apache-2.0"], "exceptions": [
            {"id": "Example.Dependency", "version": "4.5.6", "use": "test", "reason": "test only", "decision": "D0"}]}))
        self.assertEqual(self.run_script("write")[0], 0)

    def test_an_experiment_only_license_is_recorded_not_restricted(self):
        self.add_package("Example.Package", "1.2.3", "MIT")
        self.add_package("Example.Dependency", "4.5.6", "LicenseRef-Proprietary")
        self.repo.write("docs/dependencies/license-policy.json", json.dumps(
            {"allowed": ["MIT"], "unrestrictedUses": ["experiment"], "exceptions": []}))
        experiment = json.loads(json.dumps(LOCK))
        del experiment["dependencies"]["net10.0"]["Example.Package"]
        experiment["dependencies"]["net10.0"]["Example.Dependency"]["type"] = "Direct"
        self.repo.write("experiments/Spike/Spike.csproj", PROJECT)
        self.repo.write("experiments/Spike/packages.lock.json", json.dumps(experiment))
        self.repo.write("tests/Example.Tests/packages.lock.json", json.dumps(
            {"version": 1, "dependencies": {"net10.0": {"Example.Package": LOCK["dependencies"]["net10.0"]["Example.Package"]}}}))
        code, output = self.run_script("write")
        self.assertEqual(code, 0, output)
        written = json.loads((self.repo.root / "docs/dependencies/register.json").read_text())
        dependency = next(p for p in written["packages"] if p["id"] == "Example.Dependency")
        self.assertEqual(dependency["license"], "LicenseRef-Proprietary")
        self.assertEqual(dependency["uses"], ["experiment"])

    def test_an_unrestricted_use_does_not_excuse_a_test_use(self):
        self.add_package("Example.Dependency", "4.5.6", "LicenseRef-Proprietary")
        self.repo.write("docs/dependencies/license-policy.json", json.dumps(
            {"allowed": ["MIT", "Apache-2.0"], "unrestrictedUses": ["experiment"], "exceptions": []}))
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("is not allowed for test use", output)

    def npm_lock(self, **overrides):
        entry = {"version": "5.0.0", "resolved": "https://registry.npmjs.org/example-tool/-/example-tool-5.0.0.tgz",
                 "integrity": "sha512-abc", "dev": True, "license": "MIT"}
        entry.update(overrides)
        return {"lockfileVersion": 3, "packages": {
            "": {"name": "example", "devDependencies": {"example-tool": "5.0.0"}},
            "node_modules/example-tool": entry}}

    def test_npm_packages_are_registered_from_the_lock(self):
        self.repo.write("package-lock.json", json.dumps(self.npm_lock()))
        self.assertEqual(self.run_script("write")[0], 0)
        written = json.loads((self.repo.root / "docs/dependencies/register.json").read_text())
        npm = [p for p in written["packages"] if p["ecosystem"] == "npm"]
        self.assertEqual(npm, [{"ecosystem": "npm", "id": "example-tool", "version": "5.0.0", "source": "https://registry.npmjs.org/",
                                "contentHash": "sha512-abc", "license": "MIT", "uses": ["development"],
                                "projects": [{"path": ".", "dependency": "direct"}]}])

    def test_an_npm_package_from_another_source_is_refused(self):
        self.repo.write("package-lock.json", json.dumps(self.npm_lock(resolved="https://example.invalid/example-tool-5.0.0.tgz")))
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("not https://registry.npmjs.org/", output)

    def test_an_npm_package_without_integrity_is_refused(self):
        self.repo.write("package-lock.json", json.dumps(self.npm_lock(integrity="sha1-abc")))
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("no sha512 integrity", output)

    def test_an_npm_package_with_a_disallowed_license_is_refused(self):
        self.repo.write("package-lock.json", json.dumps(self.npm_lock(license="GPL-3.0-only")))
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("is not allowed for development use", output)

    def test_an_or_expression_needs_one_allowed_alternative(self):
        self.assertTrue(register.license_allowed("GPL-2.0-only OR MIT", {"MIT"}))
        self.assertFalse(register.license_allowed("MIT AND GPL-2.0-only", {"MIT"}))
        self.assertFalse(register.license_allowed({"url": "https://example.invalid/license"}, {"MIT"}))

    def test_a_package_from_another_source_is_refused(self):
        self.add_package("Example.Dependency", "4.5.6", "Apache-2.0", source="https://example.invalid/v3/index.json")
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("not https://api.nuget.org/v3/index.json", output)

    def test_a_package_that_was_not_restored_is_refused(self):
        (self.packages / "example.dependency" / "4.5.6" / "example.dependency.nuspec").unlink()
        code, output = self.run_script("write")
        self.assertEqual(code, 1)
        self.assertIn("run a locked restore first", output)


@contextlib.contextmanager
def mock_environment(**values):
    saved = {k: os.environ.get(k) for k in values}
    os.environ.update(values)
    try:
        yield
    finally:
        for key, value in saved.items():
            if value is None:
                os.environ.pop(key, None)
            else:
                os.environ[key] = value


if __name__ == "__main__":
    unittest.main()
