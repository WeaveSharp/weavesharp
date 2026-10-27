"""Tests for the restore-graph check (SEC-02): MSBuild's own evaluation catches what text can't.

    python3 .github/scripts/tests/test_restore_graph.py

Each test builds a small repository, changes it the way a hostile or mistaken
change could, and shows NuGet's restore graph refusing it. They need the .NET
SDK and no network.
"""

import importlib.util
import pathlib
import shutil
import sys
import unittest

sys.dont_write_bytecode = True
TESTS = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("test_supply_chain", TESTS / "test_supply_chain.py")
helpers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helpers)
spec = importlib.util.spec_from_file_location("check_restore_graph", TESTS.parent / "check-restore-graph.py")
graph = importlib.util.module_from_spec(spec)
spec.loader.exec_module(graph)

BUILD_PROPS = helpers.BUILD_PROPS.replace("<PropertyGroup>", "<PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>", 1)
PACKAGES_PROPS = helpers.PACKAGES_PROPS.replace("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>",
    "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally><CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>")


@unittest.skipUnless(shutil.which("dotnet"), "needs the .NET SDK")
class RestoreGraph(unittest.TestCase):
    def setUp(self):
        self.folder = helpers.tempfile.TemporaryDirectory()
        self.repo = helpers.Repository(self.folder.name)
        self.repo.write("Directory.Build.props", BUILD_PROPS)
        self.repo.write("Directory.Packages.props", PACKAGES_PROPS)

    def tearDown(self):
        self.folder.cleanup()

    def problems(self):
        return graph.check(self.repo.root)[0]

    def assertRefused(self, fragment):
        problems = self.problems()
        self.assertTrue(any(fragment in p for p in problems), f"expected {fragment!r} in {problems}")

    def test_the_compliant_repository_holds(self):
        self.assertEqual(self.problems(), [])

    def test_a_nested_directory_build_props_that_drops_the_root_settings_is_refused(self):
        self.repo.write("tests/Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n")
        self.assertRefused("does not treat warnings as errors")
        self.assertRefused("restores without a locked lock file")

    def test_later_conditional_and_target_overrides_in_the_root_are_refused(self):
        overrides = {
            "<PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup>": "audits as",
            "<Choose><When Condition=\"'1'=='2'\" /><Otherwise><PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Otherwise></Choose>": "does not treat warnings as errors",
            "<PropertyGroup><nugetauditlevel>critical</nugetauditlevel></PropertyGroup>": "audits as",
            "<PropertyGroup><RestoreSources>https://example.invalid/feed</RestoreSources></PropertyGroup>": "nuget.org is the only source",
        }
        for body, fragment in overrides.items():
            with self.subTest(body=body):
                self.repo.write("Directory.Build.props", BUILD_PROPS.replace("</Project>", body + "\n</Project>"))
                self.assertRefused(fragment)

    def test_a_latin1_user_file_that_turns_off_warnings_as_errors_is_refused(self):
        text = '<?xml version="1.0" encoding="iso-8859-1"?>\n<!-- caf\xe9 -->\n<Project><PropertyGroup><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Project>\n'
        (self.repo.root / "tests/Example.Tests/Example.Tests.csproj.user").write_bytes(text.encode("iso-8859-1"))
        self.assertRefused("does not treat warnings as errors")

    def test_central_file_exemptions_are_refused(self):
        cases = {
            '<ItemGroup><PackageDownload Include="Example.Downloaded" Version="[1.0.0]" /></ItemGroup>': "downloads packages outside its lock file",
            '<ItemGroup><PackageVersion Update="Example.Package"><Version>1.*</Version></PackageVersion></ItemGroup>': "versions are exact and central",
            '<ItemGroup><GlobalPackageReference Include="Example.Global" Version="1.*" /></ItemGroup>': "versions are exact and central",
        }
        for body, fragment in cases.items():
            with self.subTest(body=body):
                self.repo.write("Directory.Packages.props", PACKAGES_PROPS.replace("</Project>", body + "\n</Project>"))
                self.assertRefused(fragment)

    def test_a_lock_file_moved_or_a_package_folder_in_the_repository_is_refused(self):
        for body, fragment in (("<NuGetLockFilePath>obj/hidden.lock.json</NuGetLockFilePath>", "moves its lock file"),
                               ("<RestorePackagesPath>$(MSBuildThisFileDirectory)packages</RestorePackagesPath>", "inside the repository")):
            with self.subTest(body=body):
                self.repo.write("tests/Example.Tests/Example.Tests.csproj",
                                helpers.PROJECT.replace("<ItemGroup>", f"<PropertyGroup>{body}</PropertyGroup><ItemGroup>", 1))
                self.assertRefused(fragment)

    def test_an_import_redirected_to_an_unread_file_is_refused(self):
        self.repo.write("build/quiet.anything", "<Project><PropertyGroup><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>\n")
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", helpers.PROJECT.replace("<ItemGroup>",
            "<PropertyGroup><AfterMicrosoftNETSdkTargets>$(MSBuildThisFileDirectory)../../build/quiet.anything</AfterMicrosoftNETSdkTargets></PropertyGroup><ItemGroup>", 1))
        self.assertRefused("audits as")


    def test_the_record_a_restore_leaves_catches_what_a_separate_evaluation_misses(self):
        # A condition true only while restoring: the separate graph evaluation never sees it.
        quiet = ("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup Condition=\"'$(MSBuildIsRestoring)' == 'true'\">"
                 "<TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Project>\n")
        self.repo.write("tests/Example.Tests/Example.Tests.csproj", quiet)
        self.repo.write("tests/Example.Tests/packages.lock.json", '{"version": 1, "dependencies": {"net10.0": {}}}')
        self.assertEqual(self.problems(), [])
        restore = helpers.subprocess.run(["dotnet", "restore", str(self.repo.root / "tests/Example.Tests/Example.Tests.csproj")],
                                         capture_output=True, text=True, env=dict(graph.os.environ, CI="true", DOTNET_CLI_TELEMETRY_OPTOUT="1"))
        self.assertEqual(restore.returncode, 0, restore.stdout[-2000:])
        problems = graph.check_restored(self.repo.root)[0]
        self.assertTrue(any("does not treat warnings as errors" in p for p in problems), problems)

    def test_a_project_without_a_restore_record_is_refused(self):
        problems = graph.check_restored(self.repo.root)[0]
        self.assertTrue(any("no obj/project.assets.json" in p for p in problems), problems)

    def test_only_advisories_the_root_suppresses_with_a_reason_may_be_skipped(self):
        spec = {"restore": {"projectStyle": "PackageReference", "sources": {graph.NUGET_ORG: {}}, "packagesPath": "/elsewhere",
                            "restoreLockProperties": {"restorePackagesWithLockFile": "true", "restoreLockedMode": True},
                            "restoreAuditProperties": {"enableAudit": "true", "auditLevel": "low", "auditMode": "all",
                                                       "suppressedAdvisories": {"https://github.com/advisories/GHSA-a": None}},
                            "warningProperties": {"allWarningsAsErrors": True},
                            "centralPackageVersionsManagementEnabled": True, "CentralPackageTransitivePinningEnabled": True}}
        refused = []
        graph.check_project("p", spec, self.repo.root, refused)
        self.assertTrue(any("suppresses advisories" in p for p in refused), refused)
        allowed = []
        graph.check_project("p", spec, self.repo.root, allowed, frozenset({"https://github.com/advisories/GHSA-a"}))
        self.assertEqual(allowed, [])

if __name__ == "__main__":
    unittest.main(verbosity=1)
