"""Check what NuGet will actually do, not what the files appear to say (SEC-02).

The supply-chain check reads files as text, and MSBuild evaluates them: case,
encodings, conditions, nested imports, targets and property functions all
change the result. This check asks MSBuild for NuGet's restore graph, the
exact input restore uses, for every project the repository holds, and refuses
any project whose effective restore is not the one the rules require.

    python3 .github/scripts/check-restore-graph.py [repository root]

It needs the .NET SDK and no network: generating the graph restores nothing.
"""

import importlib.util
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
NUGET_ORG = "https://api.nuget.org/v3/index.json"
EXACT = r"\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?"
EXACT_RANGE = re.compile(rf"^\[{EXACT}(?:, \)|\])$")
EXACT_VERSION = re.compile(rf"^{EXACT}$")
AUDIT_CODE = re.compile(r"^NU19\d\d$", re.IGNORECASE)
TARGETING_PACK = re.compile(r"^Microsoft\.[A-Za-z]+\.App\.Ref$")
SINGLE_VERSION = re.compile(rf"^\[({EXACT}), \1\]$")


def supply_chain():
    spec = importlib.util.spec_from_file_location("check_supply_chain", pathlib.Path(__file__).with_name("check-supply-chain.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def generate(target, output):
    environment = dict(os.environ, CI="true", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    run = subprocess.run(["dotnet", "msbuild", str(target), "-t:GenerateRestoreGraphFile",
                          f"-p:RestoreGraphOutputPath={output}", "-nologo", "-v:q"],
                         capture_output=True, text=True, env=environment)
    if run.returncode != 0 or not output.is_file():
        return None, (run.stdout + run.stderr).strip().splitlines()[-3:]
    return json.loads(output.read_text(encoding="utf-8")).get("projects", {}), []


def check_project(name, spec, root, problems):
    restore = spec.get("restore", {})
    def refuse(message):
        problems.append(f"{name}: {message}")
    if restore.get("projectStyle") != "PackageReference":
        refuse(f"restores as {restore.get('projectStyle')!r}; only PackageReference projects are allowed")
    sources = list((restore.get("sources") or {}).keys())
    if sources != [NUGET_ORG]:
        refuse(f"restores from {sources}; nuget.org is the only source")
    if restore.get("fallbackFolders"):
        refuse(f"uses fallback folders {restore['fallbackFolders']}")
    packages = pathlib.Path(restore.get("packagesPath") or "/").resolve()
    if packages == root or root in packages.parents:
        refuse("restores packages into a folder inside the repository")
    locks = restore.get("restoreLockProperties") or {}
    if str(locks.get("restorePackagesWithLockFile")).lower() != "true" or locks.get("restoreLockedMode") is not True:
        refuse(f"restores without a locked lock file ({locks})")
    if locks.get("nuGetLockFilePath"):
        refuse(f"moves its lock file to {locks['nuGetLockFilePath']!r}")
    audit = restore.get("restoreAuditProperties") or {}
    if (str(audit.get("enableAudit")).lower(), audit.get("auditLevel"), audit.get("auditMode")) != ("true", "low", "all"):
        refuse(f"audits as {audit}; the audit covers all packages at level low")
    if audit.get("suppressedAdvisories"):
        refuse(f"suppresses advisories {sorted(audit['suppressedAdvisories'])}")
    warnings = restore.get("warningProperties") or {}
    if warnings.get("allWarningsAsErrors") is not True:
        refuse("does not treat warnings as errors, so an advisory would not stop the restore")
    for kind in ("noWarn", "warnNotAsError"):
        silenced = [code for code in warnings.get(kind) or [] if AUDIT_CODE.match(code)]
        if silenced:
            refuse(f"lists {silenced} in {kind}")
    if restore.get("centralPackageVersionsManagementEnabled") is not True or restore.get("CentralPackageTransitivePinningEnabled") is not True:
        refuse("does not use central versions with transitive pinning")
    for framework, details in (spec.get("frameworks") or {}).items():
        # The SDK downloads the targeting packs of an older target framework (Microsoft.*.App.Ref) at the
        # exact version it knows; nothing else may be downloaded outside a lock file.
        downloads = [d for d in details.get("downloadDependencies") or []
                     if not (TARGETING_PACK.match(str(d.get("name"))) and SINGLE_VERSION.match(str(d.get("version"))))]
        if downloads:
            refuse(f"{framework} downloads packages outside its lock file: {downloads}")
        for package, dependency in (details.get("dependencies") or {}).items():
            if dependency.get("autoReferenced"):
                continue
            if not EXACT_RANGE.match(str(dependency.get("version", ""))) or dependency.get("versionCentrallyManaged") is not True:
                refuse(f"{framework} depends on {package} {dependency.get('version')!r}; versions are exact and central")
        for package, version in (details.get("centralPackageVersions") or {}).items():
            if not EXACT_VERSION.match(str(version)):
                refuse(f"{framework} pins {package} at {version!r}; central versions are exact")


def check(root):
    root = pathlib.Path(root).resolve()
    rules = supply_chain()
    files = rules.repository_files(root)
    projects = sorted(p.resolve() for p in rules.msbuild_files(files, root) if rules.is_project(p))
    solutions = sorted(p for p in files if p.suffix.lower() in (".slnx", ".sln"))
    problems, graph = [], {}
    with tempfile.TemporaryDirectory() as scratch:
        for index, target in enumerate(solutions + [p for p in projects]):
            if target in projects and str(target) in graph:
                continue
            found, errors = generate(target, pathlib.Path(scratch) / f"graph-{index}.json")
            if found is None:
                problems.append(f"{target.relative_to(root).as_posix()}: MSBuild could not produce its restore graph: {' '.join(errors)}")
                continue
            graph.update({str(pathlib.Path(k).resolve()): v for k, v in found.items()})
    for project in projects:
        if str(project) not in graph:
            problems.append(f"{project.relative_to(root).as_posix()}: has no entry in NuGet's restore graph")
    for path, spec in sorted(graph.items()):
        resolved = pathlib.Path(path)
        name = resolved.relative_to(root).as_posix() if root in resolved.parents else path
        check_project(name, spec, root, problems)
    return problems, len(graph)


def main(argv):
    problems, count = check(argv[1] if len(argv) > 1 else ".")
    for problem in problems:
        print(f"restore graph: {problem}")
    if problems:
        print(f"{len(problems)} restore-graph rule(s) broken; see docs/dependencies/README.md.")
        return 1
    print(f"NuGet's restore graph holds the rules for all {count} projects.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
