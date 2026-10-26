#!/usr/bin/env python3
"""Check WeaveSharp's supply-chain rules (SEC-02) and list every violation.

    python3 .github/scripts/check-supply-chain.py [repository root]

Exits 0 when every rule holds and 1 otherwise. It reads every file git does not
ignore, tracked or not, so nothing outside .gitignore escapes it. The rules:

- NuGet versions are exact and set only in the root Directory.Packages.props.
- Every project restores with a lock file at its default path, and its
  packages.lock.json is committed.
- No MSBuild file adds package sources or fallback folders, moves a lock file,
  turns lock files or central versions off, imports another file, names an SDK
  that could come from a feed, or references or versions a package outside the
  central file; no response file or nested central-version file exists; and
  global.json names no MSBuild SDKs.
- NuGet.Config clears inherited sources, uses nuget.org alone and maps every
  package to it, and no other NuGet configuration file exists.
- npm uses the npmjs.org registry and never runs install scripts; every
  package.json has a committed package-lock.json whose every package resolves
  from that registry with a sha512 integrity.
- Workflows restore .NET packages only in locked mode, install npm packages only
  with `npm ci --ignore-scripts`, and run no other package installer, no
  downloaded script, and no source or registry override.
"""

import json
import os
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

# Used only outside a git work tree; inside one, .gitignore decides.
IGNORED_WITHOUT_GIT = {".git", "bin", "obj", "node_modules", ".vericairn", "TestResults"}
PROJECT_SUFFIXES = {".csproj", ".fsproj", ".vbproj", ".proj"}
IMPORTED_SUFFIXES = {".props", ".targets", ".projitems"}
MSBUILD_SUFFIXES = PROJECT_SUFFIXES | IMPORTED_SUFFIXES
# Properties and metadata that change how warnings are reported. They may list only literal codes,
# so no property indirection can assemble an audit code the text rule wouldn't see.
WARNING_LISTS = {"NoWarn", "WarningsNotAsErrors", "WarningsAsErrors", "MSBuildWarningsAsMessages",
                 "MSBuildWarningsNotAsErrors", "MSBuildWarningsAsErrors"}
WARNING_CODE = re.compile(r"^[A-Za-z]{2,}[0-9]{1,5}$")
AUDIT_CODE = re.compile(r"NU19\d\d", re.IGNORECASE)
MAX_XML_BYTES = 16 * 1024 * 1024
# Properties that make MSBuild import a file the rules never read, or skip the root files.
IMPORT_PROPERTIES = ("CustomBeforeMicrosoftCommonProps", "CustomAfterMicrosoftCommonProps",
                     "CustomBeforeMicrosoftCommonTargets", "CustomAfterMicrosoftCommonTargets",
                     "CustomBeforeMicrosoftCSharpTargets", "CustomAfterMicrosoftCSharpTargets",
                     "DirectoryBuildPropsPath", "DirectoryBuildTargetsPath", "DirectoryPackagesPropsPath",
                     "ImportDirectoryBuildProps", "ImportDirectoryBuildTargets", "ImportDirectoryPackagesProps",
                     "ImportProjectExtensionProps", "ImportProjectExtensionTargets", "MSBuildProjectExtensionsPath",
                     "MSBuildExtensionsPath", "MSBuildExtensionsPath32", "MSBuildExtensionsPath64", "MSBuildUserExtensionsPath",
                     "ForceImportBeforeCppTargets", "ForceImportAfterCppTargets", "AlternateCommonProps",
                     "AfterMicrosoftNETSdkTargets", "BeforeMicrosoftNETSdkTargets", "AfterMicrosoftNetSdkProps",
                     "CustomBeforeDirectoryBuildProps", "CustomAfterDirectoryBuildProps", "CustomAfterDirectoryBuildTargets",
                     "CustomBeforeMicrosoftCommonCrossTargetingTargets", "CustomAfterMicrosoftCommonCrossTargetingTargets",
                     "CustomBeforeMicrosoftVisualBasicTargets", "CustomAfterMicrosoftVisualBasicTargets",
                     "LanguageTargets", "NuGetRestoreTargets", "CSharpTargetsPath", "CommonTargetsPath",
                     "CodeAnalysisTargets", "VSTestTargets", "BeforeTargetFrameworkInferenceTargets", "RestoreTaskAssemblyFile")
# The audit and the warnings that make it fail a build: set only in the root Directory.Build.props.
AUDIT_SETTINGS = {"NuGetAudit": "true", "NuGetAuditMode": "all", "NuGetAuditLevel": "low", "TreatWarningsAsErrors": "true"}
# The SDK adds local folders as package sources on some machines unless these are true (root only).
IMPLICIT_SOURCES_OFF = ("DisableImplicitLibraryPacksFolder", "DisableImplicitNuGetFallbackFolder")
# npm runs these around the scripts CI calls by name, and on install.
SCRIPT_HOOKS = re.compile(r"^(pre|post)|^(install|prepare|prepublish|prepublishOnly|prepack|postpack|dependencies)$")
SOURCE_PROPERTIES = ("RestoreSources", "RestoreAdditionalProjectSources", "RestoreFallbackFolders",
                     "RestoreAdditionalProjectFallbackFolders", "RestoreConfigFile", "NuGetLockFilePath",
                     "RestorePackagesPath", "RestoreLockedMode", "RestorePackagesWithLockFile",
                     "ManagePackageVersionsCentrally", "CentralPackageTransitivePinningEnabled",
                     "CentralPackageVersionOverrideEnabled", "DisableImplicitNuGetFallbackFolder", "DisableImplicitLibraryPacksFolder",
                     "RestoreRootConfigDirectory")
# Where the lock and central-version settings may be set, and which ones.
ROOT_SETTINGS = {
    "Directory.Build.props": {"RestorePackagesWithLockFile", "RestoreLockedMode", *AUDIT_SETTINGS, *IMPLICIT_SOURCES_OFF},
    "Directory.Packages.props": {"ManagePackageVersionsCentrally", "CentralPackageTransitivePinningEnabled"},
}
ALLOWED_SDKS = {"Microsoft.NET.Sdk", "Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Razor", "Microsoft.NET.Sdk.WindowsDesktop"}
NUGET_ORG = "https://api.nuget.org/v3/index.json"
NPM_REGISTRY = "https://registry.npmjs.org/"
EXACT_VERSION = re.compile(r"^\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?$")


def repository_files(root):
    """Every file git does not ignore, tracked or untracked; or, outside git, every file not in a build folder."""
    try:
        out = subprocess.run(["git", "-C", str(root), "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
                             capture_output=True, check=True)
        return sorted({root / p for p in out.stdout.decode("utf-8").split("\0") if p and (root / p).is_file()})
    except (OSError, subprocess.CalledProcessError):
        files = []
        for directory, subdirectories, names in os.walk(root):
            subdirectories[:] = sorted(d for d in subdirectories if d not in IGNORED_WITHOUT_GIT)
            files.extend(pathlib.Path(directory) / name for name in sorted(names))
        return files


def tracked_files(root):
    """The repository's tracked paths, or None outside a git work tree."""
    try:
        out = subprocess.run(["git", "-C", str(root), "ls-files", "-z"], capture_output=True, check=True)
    except (OSError, subprocess.CalledProcessError):
        return None
    return {p for p in out.stdout.decode("utf-8").split("\0") if p}


def xml_text(path):
    """A file's text when it could be XML in an encoding MSBuild reads (UTF-8, UTF-16 or UTF-32, with or
    without a byte-order mark), with any XML declaration removed; otherwise None."""
    try:
        if path.stat().st_size > MAX_XML_BYTES:
            return None
        data = path.read_bytes()
    except OSError:
        return None
    if data.startswith((b"\xff\xfe\x00\x00", b"\x00\x00\xfe\xff")):
        candidates = ["utf-32"]
    elif data.startswith((b"\xff\xfe", b"\xfe\xff")):
        candidates = ["utf-16"]
    elif b"\x00" in data[:64]:
        candidates = ["utf-32-le", "utf-32-be", "utf-16-le", "utf-16-be"]
    else:
        candidates = ["utf-8-sig"]
    for encoding in candidates:
        try:
            text = data.decode(encoding)
        except UnicodeDecodeError:
            continue
        text = re.sub(r"^\s*<\?xml[^>]*\?>", "", text.lstrip("\ufeff")).lstrip()
        if text.startswith("<"):
            return text
    return None


def xml_root(path):
    """The local name of a file's root element when it is well-formed XML in any encoding, else None."""
    text = xml_text(path)
    try:
        if text is not None:
            return local(ET.fromstring(text).tag)
        if path.stat().st_size <= MAX_XML_BYTES:
            return local(ET.parse(path).getroot().tag)  # an encoding the declaration names, such as ISO-8859-1
    except (ET.ParseError, OSError, ValueError):
        pass
    return None


def solution_projects(root, files):
    """Every project path a solution file lists, whatever its extension or content."""
    listed = set()
    for solution in (p for p in files if p.suffix.lower() in (".slnx", ".sln")):
        try:
            text = solution.read_text(encoding="utf-8-sig", errors="replace")
        except OSError:
            continue
        if solution.suffix.lower() == ".slnx":
            decoded = xml_text(solution)
            try:
                tree = ET.fromstring(decoded) if decoded is not None else ET.parse(solution).getroot()
            except (ET.ParseError, ValueError):
                tree = None
            paths = [] if tree is None else [value for element in tree.iter() if local(element.tag).lower() == "project"
                                             for key, value in element.attrib.items() if key.lower() == "path"]
        else:
            paths = re.findall(r'^\s*Project\("\{[^}]+\}"\)\s*=\s*"[^"]*",\s*"([^"]+)"', text, re.MULTILINE | re.IGNORECASE)
        for path in paths:
            candidate = (solution.parent / path.replace("\\", "/")).resolve()
            if candidate.is_file():
                listed.add(candidate)
    return listed


def msbuild_files(files, root=None):
    """Every file MSBuild could read as a project or import: known extensions, any *proj,
    any file whose XML root is <Project>, and every project a solution lists."""
    listed = solution_projects(root, files) if root is not None else set()
    found = []
    for path in files:
        if (path.suffix in MSBUILD_SUFFIXES or path.suffix.endswith("proj") or path.resolve() in listed
                or (xml_root(path) or "").lower() == "project"):
            found.append(path)
    return found


def is_project(path):
    return path.suffix not in IMPORTED_SUFFIXES


def local(tag):
    return tag.rsplit("}", 1)[-1]


def parse_xml(path, problems):
    try:
        return ET.parse(path).getroot()
    except ET.ParseError as error:
        problems.append(f"{path.name}: not well-formed XML ({error})")
        return None


# Every MSBuild name the rules compare. MSBuild matches property, item, metadata and attribute names
# without regard to case, so each parsed MSBuild file has them rewritten to these spellings first.
CANONICAL = {name.lower(): name for name in (
    *SOURCE_PROPERTIES, *AUDIT_SETTINGS, *WARNING_LISTS, *IMPORT_PROPERTIES,
    "Project", "PropertyGroup", "ItemGroup", "ImportGroup", "Import", "Sdk", "Target", "Choose", "When", "Otherwise",
    "PackageReference", "PackageDownload", "GlobalPackageReference", "PackageVersion", "NuGetAuditSuppress",
    "Include", "Update", "Remove", "Exclude", "Version", "VersionOverride", "Condition", "Reason", "Name",
)}


def parse_msbuild(path, problems):
    """An MSBuild file, parsed from any encoding, with the names the rules compare in canonical case."""
    text = xml_text(path)
    try:
        xml = ET.fromstring(text) if text is not None else ET.parse(path).getroot()
    except ET.ParseError as error:
        problems.append(f"{path.name}: not well-formed XML ({error})")
        return None
    for element in xml.iter():
        name = local(element.tag)
        element.tag = CANONICAL.get(name.lower(), name)
        renamed = {CANONICAL.get(key.lower(), key): value for key, value in element.attrib.items()}
        element.attrib.clear()
        element.attrib.update(renamed)
    return xml


def property_value(root, name):
    for element in root.iter():
        if local(element.tag) == name and element.text is not None:
            return element.text.strip()
    return None


def check_central_versions(root, files, problems):
    props = root / "Directory.Packages.props"
    for other in (p for p in files if p.name == "Directory.Packages.props" and p != props):
        problems.append(f"{other.relative_to(root).as_posix()}: only the root Directory.Packages.props may set versions")
    if not props.is_file():
        problems.append("Directory.Packages.props is missing; package versions must be central")
        return
    xml = parse_msbuild(props, problems)
    if xml is None:
        return
    if (property_value(xml, "ManagePackageVersionsCentrally") or "").lower() != "true":
        problems.append("Directory.Packages.props must set ManagePackageVersionsCentrally to true")
    check_root_shape("Directory.Packages.props", xml, ROOT_SETTINGS["Directory.Packages.props"], problems)
    for element in xml.iter():
        if local(element.tag) != "PackageVersion":
            continue
        name = element.get("Include") or element.get("Update") or "?"
        version = element.get("Version")
        if version is None or not EXACT_VERSION.match(version):
            problems.append(f"Directory.Packages.props: {name} must have an exact version, not {version!r}")
        if any(local(child.tag) in ("Version", "VersionOverride") for child in element):
            problems.append(f"Directory.Packages.props: {name} sets its version in a child element; versions are attributes")


def check_root_shape(file_name, xml, settings, problems):
    """Each required setting appears once, as a direct child of a top-level PropertyGroup; no Choose or Target."""
    for name in sorted(settings):
        places = [e for e in xml.iter() if local(e.tag) == name]
        direct = [e for group in xml if local(group.tag) == "PropertyGroup" for e in group if local(e.tag) == name]
        if len(places) > 1 or places != direct:
            problems.append(f"{file_name}: {name} must be set exactly once, directly in a top-level PropertyGroup")
    for element in xml.iter():
        if local(element.tag) in ("Choose", "When", "Otherwise", "Target", "ImportGroup"):
            problems.append(f"{file_name}: has a {local(element.tag)} element; the root settings hold unconditionally")


def check_build_props(root, problems):
    props = root / "Directory.Build.props"
    if not props.is_file():
        problems.append("Directory.Build.props is missing; it must turn lock files on")
        return
    xml = parse_msbuild(props, problems)
    if xml is None:
        return
    if (property_value(xml, "RestorePackagesWithLockFile") or "").lower() != "true":
        problems.append("Directory.Build.props must set RestorePackagesWithLockFile to true")
    for name, value in AUDIT_SETTINGS.items():
        if (property_value(xml, name) or "").lower() != value:
            problems.append(f"Directory.Build.props must set {name} to {value}")
    for name in IMPLICIT_SOURCES_OFF:
        if (property_value(xml, name) or "").lower() != "true":
            problems.append(f"Directory.Build.props must set {name} to true")
    check_root_shape("Directory.Build.props", xml, {"RestorePackagesWithLockFile", "RestoreLockedMode", *AUDIT_SETTINGS, *IMPLICIT_SOURCES_OFF}, problems)
    # The settings that keep restores locked and audited hold unconditionally; only locked mode waits for CI.
    for group in (e for e in xml.iter() if local(e.tag) == "PropertyGroup"):
        for element in group:
            name = local(element.tag)
            if name in AUDIT_SETTINGS or name == "RestorePackagesWithLockFile":
                if group.get("Condition") is not None or element.get("Condition") is not None:
                    problems.append(f"Directory.Build.props: {name} must not carry a Condition")
            if name == "RestoreLockedMode" and (group.get("Condition") is not None or
                                               (element.get("Condition") or "").replace(" ", "") not in ("", "'$(CI)'=='true'")):
                problems.append("Directory.Build.props: RestoreLockedMode may be conditioned only on CI")


def check_projects(root, files, tracked, problems):
    projects = [p for p in msbuild_files(files, root) if is_project(p)]
    folders = {}
    for project in projects:
        folders.setdefault(project.parent, []).append(project)
    for folder, members in sorted(folders.items()):
        if len(members) > 1:
            names = ", ".join(p.name for p in sorted(members))
            problems.append(f"{folder.relative_to(root).as_posix() or '.'}: holds {len(members)} projects ({names}); "
                            "one project per folder keeps each lock file where restore and CI look for it")
    for project in projects:
        relative = project.relative_to(root).as_posix()
        xml = parse_msbuild(project, problems)
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
        lock = project.parent / "packages.lock.json"
        lock_relative = lock.relative_to(root).as_posix()
        if not lock.is_file():
            problems.append(f"{relative}: has no packages.lock.json; restore it and commit the lock file")
        elif tracked is not None and lock_relative not in tracked:
            problems.append(f"{lock_relative}: exists but is not committed")


def check_msbuild_files(root, files, problems):
    """Package sources come only from NuGet.Config, and every MSBuild file stays inside the rules."""
    for path in files:
        relative = path.relative_to(root).as_posix()
        lower = path.name.lower()
        if path.suffix.lower() == ".rsp":
            problems.append(f"{relative}: response files can change every restore; none may exist")
        if path.suffix.lower() == ".user":
            problems.append(f"{relative}: MSBuild imports .user files beside projects; none may be committed")
        if re.match(r"^directory\.(build|packages|solution)\.(props|targets)$", lower) and not (
                path.parent == root and path.name in ("Directory.Build.props", "Directory.Packages.props")):
            problems.append(f"{relative}: MSBuild imports this by name; only the root Directory.Build.props and "
                            "Directory.Packages.props may exist")
        try:
            if path.stat().st_size > MAX_XML_BYTES:
                problems.append(f"{relative}: over 16 MB; the checks read every file MSBuild could, so none may be larger")
        except OSError:
            pass
    for path in msbuild_files(files, root):
        relative = path.relative_to(root).as_posix()
        is_root = path.parent == root and path.name in ROOT_SETTINGS
        xml = parse_msbuild(path, problems)
        if xml is None:
            continue
        # Parsed values: character references and encodings are already decoded here.
        values = [v for e in xml.iter() for v in (e.text, e.tail, *e.attrib.values()) if v]
        if any(AUDIT_CODE.search(v) for v in values):
            problems.append(f"{relative}: names a NuGet audit warning (NU1900-NU1999); advisories are never silenced, "
                            "only suppressed by advisory with a reason in the root Directory.Build.props")
        for element in xml.iter():
            lists = [(local(element.tag), element.text or "")] + [(a, v) for a, v in element.attrib.items() if a in WARNING_LISTS]
            for name, value in lists:
                if name not in WARNING_LISTS:
                    continue
                for token in (t for t in re.split(r"[;,\s]+", value) if t):
                    if token != f"$({name})" and not WARNING_CODE.match(token):
                        problems.append(f"{relative}: {name} holds {token!r}; warning lists name literal codes only, "
                                        f"besides $({name}) itself")
        sdk = xml.get("Sdk")
        if sdk is not None and (sdk.lower() not in {a.lower() for a in ALLOWED_SDKS} or "/" in sdk):
            problems.append(f"{relative}: names SDK {sdk!r}; only versionless Microsoft.NET SDKs are allowed")
        for element in xml.iter():
            name = local(element.tag)
            if name in SOURCE_PROPERTIES and not (is_root and name in ROOT_SETTINGS[path.name]):
                problems.append(f"{relative}: sets {name}; package sources and folders come only from NuGet.Config, "
                                "and lock and central-version settings only from the root files")
            if name in IMPORT_PROPERTIES:
                problems.append(f"{relative}: sets {name}; MSBuild files import nothing the rules don't read")
            if name in AUDIT_SETTINGS and not (is_root and path.name == "Directory.Build.props"):
                problems.append(f"{relative}: sets {name}; the audit and warnings-as-errors are set only in the root Directory.Build.props")
            if name == "NuGetAuditSuppress":
                reason = element.get("Reason") or next((c.text for c in element if local(c.tag) == "Reason"), None)
                if not (is_root and path.name == "Directory.Build.props"):
                    problems.append(f"{relative}: suppresses an advisory; suppressions belong in the root Directory.Build.props")
                elif not (reason or "").strip():
                    problems.append(f"{relative}: suppresses {element.get('Include') or '?'} without a Reason")
            if name in ("Import", "Sdk"):
                problems.append(f"{relative}: has an {name} element; MSBuild files import nothing beyond their SDK")
            if name in ("GlobalPackageReference", "PackageDownload"):
                item = element.get("Include") or element.get("Update") or "?"
                problems.append(f"{relative}: declares {name} {item}; every package is a locked PackageReference")
            elif name == "PackageVersion" and path.name != "Directory.Packages.props":
                item = element.get("Include") or element.get("Update") or "?"
                problems.append(f"{relative}: declares {name} {item}; versions belong in Directory.Packages.props")
            elif name == "PackageReference" and not is_project(path):
                item = element.get("Include") or element.get("Update") or "?"
                problems.append(f"{relative}: declares PackageReference {item}; packages belong in projects")
    global_json = root / "global.json"
    if global_json.is_file():
        try:
            if "msbuild-sdks" in json.loads(global_json.read_text(encoding="utf-8")):
                problems.append("global.json: msbuild-sdks would restore SDKs from a feed; none may be named")
        except json.JSONDecodeError:
            problems.append("global.json: not valid JSON")


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
    if any(local(e.tag) in ("disabledPackageSources", "packageSourceCredentials", "config") for e in xml):
        problems.append("NuGet.Config: no disabled sources, source credentials or config settings")
    # Machine-wide configurations (Visual Studio's, for one) add fallback folders; the root clears them all.
    fallback = [e for e in xml if local(e.tag) == "fallbackPackageFolders"]
    if len(fallback) != 1 or [local(c.tag) for c in fallback[0]] != ["clear"]:
        problems.append("NuGet.Config: fallbackPackageFolders must hold exactly <clear />")
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
    if settings.get("registry") != NPM_REGISTRY:
        problems.append(f".npmrc must set registry={NPM_REGISTRY}")
    for name in settings:
        if name.startswith("@") or name.startswith("//") or name in ("strict-ssl", "ca", "cafile", "proxy", "https-proxy"):
            problems.append(f".npmrc: {name} would change where or how packages are fetched")
    for other in (p for p in files if p.name == ".npmrc" and p != npmrc):
        problems.append(f"{other.relative_to(root).as_posix()}: only the root .npmrc may configure npm")
    for package in (p for p in files if p.name == "package.json"):
        lock = package.parent / "package-lock.json"
        lock_relative = lock.relative_to(root).as_posix()
        if not lock.is_file():
            problems.append(f"{package.relative_to(root).as_posix()}: has no package-lock.json")
            continue
        if tracked is not None and lock_relative not in tracked:
            problems.append(f"{lock_relative}: exists but is not committed")
        try:
            data = json.loads(lock.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            problems.append(f"{lock_relative}: not valid JSON")
            continue
        if data.get("lockfileVersion", 0) < 2:
            problems.append(f"{lock_relative}: lockfileVersion 2 or later is required")
        for key, info in data.get("packages", {}).items():
            if not key or info.get("link"):
                continue
            if not str(info.get("resolved", "")).startswith(NPM_REGISTRY):
                problems.append(f"{lock_relative}: {key} does not resolve from {NPM_REGISTRY}")
            if not str(info.get("integrity", "")).startswith("sha512-"):
                problems.append(f"{lock_relative}: {key} has no sha512 integrity")
    for other in (p for p in files if p.name in ("yarn.lock", "pnpm-lock.yaml", "npm-shrinkwrap.json")):
        problems.append(f"{other.relative_to(root).as_posix()}: npm with package-lock.json is the only JavaScript package manager")


WORKFLOW_RULES = [
    (re.compile(r"\bnpm\s+(install|i|add|update|exec)\b"), "use npm ci --ignore-scripts, never npm install or npm exec"),
    (re.compile(r"\bnpx\b"), "npx fetches and runs packages; it is not used"),
    (re.compile(r"\b(yarn|pnpm|bun)\b"), "npm is the only JavaScript package manager"),
    (re.compile(r"\b(pip3?|pipx|uv|poetry)\s+(install|add|run|sync)\b|\bpython3?\s+-m\s+pip\b"), "workflows install no Python packages"),
    (re.compile(r"\bdotnet\s+tool\b"), "workflows install and run no .NET tools"),
    (re.compile(r"\b(curl|wget|iwr|Invoke-WebRequest)\b.*\|\s*(sudo\s+)?(sh|bash|zsh|pwsh|iex|python3?)\b"), "workflows never run a downloaded script"),
    (re.compile(r"--ignore-scripts\s*=\s*(false|0)\b"), "npm ci must use --ignore-scripts"),
    (re.compile(r"^\s*(source-url|registry-url|config-file)\s*:"), "a setup action may not point at another package source"),
    (re.compile(r"^\s*(NUGET_[A-Z_]*|NPM_CONFIG_[A-Z_]*|npm_config_[a-z_]*|RestoreSources|RestoreAdditionalProjectSources|RestoreConfigFile)\s*:"),
     "workflows set no package-source environment"),
]


def command_lines(text):
    """Logical command lines: shell and PowerShell continuations joined, quotes removed, comment lines dropped."""
    lines = []
    pending, start = "", 0
    for number, line in enumerate(text.splitlines(), 1):
        if not pending:
            start = number
        if line.lstrip().startswith("#") and not pending:
            continue
        # A continued line joins the next one with nothing between them. YAML removes a block's
        # indentation before the shell sees it, so the next line's leading space is dropped too:
        # "n\\" then "px" in a run block reaches the shell as "npx".
        stripped = line.rstrip()
        piece = line.lstrip() if pending else line
        if stripped.endswith("\\") or stripped.endswith("`"):
            pending += (stripped.lstrip() if pending else stripped)[:-1]
            continue
        lines.append((start, unquote(pending + piece)))
        pending = ""
    if pending:
        lines.append((start, unquote(pending)))
    return lines


def unquote(text):
    """Drops an inline comment (a # after whitespace, outside quotes), then every quote character."""
    quote = None
    for index, character in enumerate(text):
        if quote:
            if character == quote:
                quote = None
        elif character in "'\"":
            quote = character
        elif character == "#" and (index == 0 or text[index - 1] in " \t"):
            text = text[:index]
            break
    return text.replace('"', "").replace("'", "")


def check_commands(where_prefix, text, problems):
    for number, code in command_lines(text):
        where = f"{where_prefix}:{number}"
        if re.search(r"\bdotnet\s+restore\b", code) and "--locked-mode" not in code:
            problems.append(f"{where}: dotnet restore must use --locked-mode")
        if re.search(r"\bdotnet\s+(restore|build|test|run|pack|publish)\b", code) and re.search(r"(\s--source\b|\s-s\s|--configfile\b|RestoreSources|RestoreConfigFile)", code):
            problems.append(f"{where}: package sources come only from NuGet.Config, never from the command line")
        if re.search(r"\bdotnet\s+nuget\s+(add|update|enable)\s+source\b", code):
            problems.append(f"{where}: workflows never add package sources")
        if re.search(r"\bnpm\s+ci\b", code) and not re.search(r"--ignore-scripts(?!\s*=)", code):
            problems.append(f"{where}: npm ci must use --ignore-scripts")
        for pattern, message in WORKFLOW_RULES:
            if pattern.search(code):
                problems.append(f"{where}: {message}")


def check_workflows(root, problems):
    workflows = root / ".github" / "workflows"
    if not workflows.is_dir():
        return
    for workflow in sorted(list(workflows.glob("*.yml")) + list(workflows.glob("*.yaml"))):
        check_commands(workflow.relative_to(root).as_posix(), workflow.read_text(encoding="utf-8"), problems)


def check_package_scripts(root, files, problems):
    """CI runs package.json scripts by name, so they keep the workflow rules, and npm's hooks never run."""
    for package in (p for p in files if p.name == "package.json"):
        relative = package.relative_to(root).as_posix()
        try:
            scripts = json.loads(package.read_text(encoding="utf-8")).get("scripts") or {}
        except (json.JSONDecodeError, AttributeError):
            problems.append(f"{relative}: not valid JSON")
            continue
        for name, command in scripts.items():
            if SCRIPT_HOOKS.search(name):
                problems.append(f"{relative}: script {name!r} is an npm hook that runs implicitly; none may exist")
            if isinstance(command, str):
                check_commands(f"{relative} scripts.{name}", command, problems)


def check(root):
    root = pathlib.Path(root).resolve()
    files = repository_files(root)
    tracked = tracked_files(root)
    problems = []
    check_central_versions(root, files, problems)
    check_build_props(root, problems)
    check_projects(root, files, tracked, problems)
    check_msbuild_files(root, files, problems)
    check_nuget_config(root, files, problems)
    check_npm(root, files, tracked, problems)
    check_workflows(root, problems)
    check_package_scripts(root, files, problems)
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
