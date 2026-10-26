#!/usr/bin/env python3
"""Check WeaveSharp's supply-chain rules (SEC-02) and list every violation.

    python3 .github/scripts/check-supply-chain.py [repository root]

Exits 0 when every rule holds and 1 otherwise. The rules:

- NuGet versions are exact and set only in Directory.Packages.props.
- Every project restores with a lock file, and its packages.lock.json is committed.
- No MSBuild file adds package sources or fallback folders, or references or
  versions a package outside the central file.
- NuGet.Config clears inherited sources, uses nuget.org alone and maps every package to it.
- No other NuGet configuration file exists in the repository.
- npm never runs install scripts, and every package.json has a committed package-lock.json.
- Workflows restore .NET packages only in locked mode and install npm packages only
  with `npm ci --ignore-scripts`.
"""

import os
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

SKIP = {".git", "bin", "obj", "node_modules", ".vericairn", "TestResults", "artifacts"}
PROJECT_SUFFIXES = {".csproj", ".fsproj", ".vbproj"}
MSBUILD_SUFFIXES = PROJECT_SUFFIXES | {".props", ".targets"}
SOURCE_PROPERTIES = ("RestoreSources", "RestoreAdditionalProjectSources", "RestoreFallbackFolders",
                     "RestoreAdditionalProjectFallbackFolders", "RestoreConfigFile")
NUGET_ORG = "https://api.nuget.org/v3/index.json"
EXACT_VERSION = re.compile(r"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$")


def walk(root):
    for directory, subdirectories, names in os.walk(root):
        subdirectories[:] = sorted(d for d in subdirectories if d not in SKIP)
        for name in sorted(names):
            yield pathlib.Path(directory) / name


def tracked_files(root):
    """The repository's tracked paths, or None outside a git work tree."""
    try:
        out = subprocess.run(["git", "-C", str(root), "ls-files", "-z"], capture_output=True, check=True)
    except (OSError, subprocess.CalledProcessError):
        return None
    return {p for p in out.stdout.decode("utf-8").split("\0") if p}


def local(tag):
    return tag.rsplit("}", 1)[-1]


def parse_xml(path, problems):
    try:
        return ET.parse(path).getroot()
    except ET.ParseError as error:
        problems.append(f"{path.name}: not well-formed XML ({error})")
        return None


def property_value(root, name):
    for element in root.iter():
        if local(element.tag) == name and element.text is not None:
            return element.text.strip()
    return None


def check_central_versions(root, problems):
    props = root / "Directory.Packages.props"
    if not props.is_file():
        problems.append("Directory.Packages.props is missing; package versions must be central")
        return
    xml = parse_xml(props, problems)
    if xml is None:
        return
    if (property_value(xml, "ManagePackageVersionsCentrally") or "").lower() != "true":
        problems.append("Directory.Packages.props must set ManagePackageVersionsCentrally to true")
    for element in xml.iter():
        if local(element.tag) != "PackageVersion":
            continue
        name = element.get("Include") or element.get("Update") or "?"
        version = element.get("Version")
        if version is None or not EXACT_VERSION.match(version):
            problems.append(f"Directory.Packages.props: {name} must have an exact version, not {version!r}")


def check_build_props(root, problems):
    props = root / "Directory.Build.props"
    if not props.is_file():
        problems.append("Directory.Build.props is missing; it must turn lock files on")
        return
    xml = parse_xml(props, problems)
    if xml is not None and (property_value(xml, "RestorePackagesWithLockFile") or "").lower() != "true":
        problems.append("Directory.Build.props must set RestorePackagesWithLockFile to true")


def check_projects(root, files, tracked, problems):
    for project in (p for p in files if p.suffix in PROJECT_SUFFIXES):
        relative = project.relative_to(root).as_posix()
        xml = parse_xml(project, problems)
        if xml is None:
            continue
        for element in xml.iter():
            if local(element.tag) not in ("PackageReference", "PackageDownload", "GlobalPackageReference"):
                continue
            name = element.get("Include") or element.get("Update") or "?"
            if local(element.tag) == "PackageDownload":
                problems.append(f"{relative}: PackageDownload {name} bypasses the lock file")
            for attribute in ("Version", "VersionOverride"):
                if element.get(attribute) is not None or any(local(c.tag) == attribute for c in element):
                    problems.append(f"{relative}: {name} sets {attribute}; versions belong in Directory.Packages.props")
        if (property_value(xml, "RestorePackagesWithLockFile") or "true").lower() == "false":
            problems.append(f"{relative}: turns lock files off")
        lock = project.parent / "packages.lock.json"
        lock_relative = lock.relative_to(root).as_posix()
        if not lock.is_file():
            problems.append(f"{relative}: has no packages.lock.json; restore it and commit the lock file")
        elif tracked is not None and lock_relative not in tracked:
            problems.append(f"{lock_relative}: exists but is not committed")


def check_msbuild_files(root, files, problems):
    """Package sources come only from NuGet.Config, and package items only from projects without versions."""
    for path in (p for p in files if p.suffix in MSBUILD_SUFFIXES):
        relative = path.relative_to(root).as_posix()
        xml = parse_xml(path, problems)
        if xml is None:
            continue
        for element in xml.iter():
            name = local(element.tag)
            if name in SOURCE_PROPERTIES:
                problems.append(f"{relative}: sets {name}; package sources and folders come only from NuGet.Config")
            if path.suffix in (".props", ".targets") and path.name != "Directory.Packages.props" and name in (
                    "PackageReference", "PackageVersion", "GlobalPackageReference", "PackageDownload"):
                item = element.get("Include") or element.get("Update") or "?"
                problems.append(f"{relative}: declares {name} {item}; packages belong in projects and versions in Directory.Packages.props")


def check_nuget_config(root, files, problems):
    configs = [p for p in files if p.name.lower() == "nuget.config"]
    expected = root / "NuGet.Config"
    for other in configs:
        if other != expected:
            problems.append(f"{other.relative_to(root).as_posix()}: only the root NuGet.Config may configure package sources")
    if not expected.is_file():
        problems.append("NuGet.Config is missing at the repository root")
        return
    xml = parse_xml(expected, problems)
    if xml is None:
        return
    sources = next((e for e in xml if local(e.tag) == "packageSources"), None)
    if sources is None or len(sources) == 0 or local(sources[0].tag) != "clear":
        problems.append("NuGet.Config: packageSources must start with <clear />")
    added = [e for e in (sources if sources is not None else []) if local(e.tag) == "add"]
    if [(e.get("value") or "") for e in added] != [NUGET_ORG]:
        problems.append(f"NuGet.Config: the only package source must be {NUGET_ORG}")
    key = added[0].get("key") if len(added) == 1 else None
    if any(local(e.tag) in ("disabledPackageSources", "fallbackPackageFolders", "packageSourceCredentials") for e in xml):
        problems.append("NuGet.Config: no disabled sources, fallback folders or source credentials")
    mapping = next((e for e in xml if local(e.tag) == "packageSourceMapping"), None)
    mapped = []
    if mapping is not None:
        for source in mapping:
            for package in source:
                mapped.append((source.get("key"), package.get("pattern")))
    if mapped != [(key, "*")]:
        problems.append("NuGet.Config: packageSourceMapping must map every package (\"*\") to nuget.org alone")


def check_npm(root, files, tracked, problems):
    npmrc = root / ".npmrc"
    settings = {}
    if npmrc.is_file():
        for line in npmrc.read_text(encoding="utf-8").splitlines():
            line = line.split("#", 1)[0].strip()
            if "=" in line:
                name, value = (part.strip() for part in line.split("=", 1))
                settings[name] = value
    if settings.get("ignore-scripts") != "true":
        problems.append(".npmrc must set ignore-scripts=true")
    for other in (p for p in files if p.name == ".npmrc" and p != npmrc):
        problems.append(f"{other.relative_to(root).as_posix()}: only the root .npmrc may configure npm")
    for package in (p for p in files if p.name == "package.json"):
        lock = package.parent / "package-lock.json"
        lock_relative = lock.relative_to(root).as_posix()
        if not lock.is_file():
            problems.append(f"{package.relative_to(root).as_posix()}: has no package-lock.json")
        elif tracked is not None and lock_relative not in tracked:
            problems.append(f"{lock_relative}: exists but is not committed")
    for other in (p for p in files if p.name in ("yarn.lock", "pnpm-lock.yaml", "npm-shrinkwrap.json")):
        problems.append(f"{other.relative_to(root).as_posix()}: npm with package-lock.json is the only JavaScript package manager")


def check_workflows(root, problems):
    workflows = root / ".github" / "workflows"
    if not workflows.is_dir():
        return
    for workflow in sorted(list(workflows.glob("*.yml")) + list(workflows.glob("*.yaml"))):
        for number, line in enumerate(workflow.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("#", 1)[0]
            where = f"{workflow.relative_to(root).as_posix()}:{number}"
            if re.search(r"\bdotnet\s+restore\b", code) and "--locked-mode" not in code:
                problems.append(f"{where}: dotnet restore must use --locked-mode")
            if re.search(r"\bdotnet\s+(restore|build|test|run|pack|publish)\b", code) and re.search(r"(\s--source\b|\s-s\s|--configfile\b|RestoreSources|RestoreConfigFile)", code):
                problems.append(f"{where}: package sources come only from NuGet.Config, never from the command line")
            if re.search(r"\bdotnet\s+nuget\s+(add|update|enable)\s+source\b", code):
                problems.append(f"{where}: workflows never add package sources")
            if re.search(r"\bnpm\s+(install|i|add|update)\b", code):
                problems.append(f"{where}: use npm ci --ignore-scripts, never npm install")
            if re.search(r"\bnpm\s+ci\b", code) and "--ignore-scripts" not in code:
                problems.append(f"{where}: npm ci must use --ignore-scripts")
            if re.search(r"\b(yarn|pnpm)\b", code):
                problems.append(f"{where}: npm is the only JavaScript package manager")


def check(root):
    root = pathlib.Path(root).resolve()
    files = list(walk(root))
    tracked = tracked_files(root)
    problems = []
    check_central_versions(root, problems)
    check_build_props(root, problems)
    check_projects(root, files, tracked, problems)
    check_msbuild_files(root, files, problems)
    check_nuget_config(root, files, problems)
    check_npm(root, files, tracked, problems)
    check_workflows(root, problems)
    return problems


def main(argv):
    root = argv[1] if len(argv) > 1 else "."
    problems = check(root)
    for problem in problems:
        print(f"supply chain: {problem}")
    if problems:
        print(f"{len(problems)} supply-chain rule(s) broken; see docs/dependencies/README.md.")
        return 1
    print("Supply-chain rules hold.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
