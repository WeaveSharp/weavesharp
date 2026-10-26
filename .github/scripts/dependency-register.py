#!/usr/bin/env python3
"""Write or check WeaveSharp's dependency register (SEC-02).

    python3 .github/scripts/dependency-register.py write [repository root]
    python3 .github/scripts/dependency-register.py check [repository root]

The register, docs/dependencies/register.json, lists every NuGet package any
project restores: its id, exact version, the source it was restored from, the
content hash its lock file pins, its license, and how WeaveSharp uses it. It is
generated from the committed packages.lock.json files and the restored
packages' own metadata, so run a locked restore first.

`check` fails when the committed register differs from what the lock files
say, when a package came from anywhere but nuget.org, or when a license is
outside docs/dependencies/license-policy.json, has no recorded exception, and is used
somewhere other than the policy's unrestricted uses (experiments, which ship nothing).
`write` regenerates the register and reports the same license problems.
"""

import json
import os
import pathlib
import re
import subprocess
import sys

SKIP = {".git", "bin", "obj", "node_modules", ".vericairn", "TestResults", "artifacts"}
NUGET_ORG = "https://api.nuget.org/v3/index.json"
REGISTER = pathlib.PurePosixPath("docs/dependencies/register.json")
POLICY = pathlib.PurePosixPath("docs/dependencies/license-policy.json")
ABOUT = ("Every NuGet package WeaveSharp's projects restore, generated from the committed lock files "
         "by .github/scripts/dependency-register.py. Do not edit it by hand; see docs/dependencies/README.md.")


def lock_files(root):
    for directory, subdirectories, names in os.walk(root):
        subdirectories[:] = sorted(d for d in subdirectories if d not in SKIP)
        if "packages.lock.json" in names:
            yield pathlib.Path(directory) / "packages.lock.json"


def use_of(project_path):
    first = project_path.parts[0] if project_path.parts else ""
    return {"tests": "test", "experiments": "experiment"}.get(first, "product")


def global_packages_folder():
    if os.environ.get("NUGET_PACKAGES"):
        return pathlib.Path(os.environ["NUGET_PACKAGES"])
    try:
        out = subprocess.run(["dotnet", "nuget", "locals", "global-packages", "--list"],
                             capture_output=True, text=True, check=True).stdout
        match = re.search(r"global-packages:\s*(.+)", out)
        if match:
            return pathlib.Path(match.group(1).strip())
    except (OSError, subprocess.CalledProcessError):
        pass
    return pathlib.Path.home() / ".nuget" / "packages"


def package_metadata(packages, name, version):
    """The license and the source a restored package came from, from its own files."""
    folder = packages / name.lower() / version.lower()
    nuspec = folder / f"{name.lower()}.nuspec"
    if not nuspec.is_file():
        return None, None
    text = nuspec.read_text(encoding="utf-8")
    license_element = re.search(r'<license\s+type="(expression|file)"[^>]*>([^<]+)</license>', text)
    if license_element and license_element.group(1) == "expression":
        license = license_element.group(2).strip()
    elif license_element:
        license = {"file": license_element.group(2).strip()}
    else:
        url = re.search(r"<licenseUrl>([^<]+)</licenseUrl>", text)
        license = {"url": url.group(1).strip()} if url else {"unknown": True}
    source = None
    metadata = folder / ".nupkg.metadata"
    if metadata.is_file():
        source = json.loads(metadata.read_text(encoding="utf-8")).get("source")
    return license, source


def build(root):
    """The register the committed lock files describe, and every problem found building it."""
    packages_folder = global_packages_folder()
    entries, problems = {}, []
    for lock in lock_files(root):
        project = lock.parent.relative_to(root)
        data = json.loads(lock.read_text(encoding="utf-8"))
        for framework, dependencies in data.get("dependencies", {}).items():
            for name, info in dependencies.items():
                if info.get("type") == "Project":
                    continue
                version = info.get("resolved")
                key = (name, version)
                entry = entries.get(key)
                if entry is None:
                    license, source = package_metadata(packages_folder, name, version)
                    if license is None:
                        problems.append(f"{name} {version}: not in the global packages folder; run a locked restore first")
                    entry = entries[key] = {"id": name, "version": version, "source": source,
                                            "contentHash": info.get("contentHash"), "license": license,
                                            "uses": set(), "projects": {}}
                elif entry["contentHash"] != info.get("contentHash"):
                    problems.append(f"{name} {version}: lock files disagree on its content hash")
                entry["uses"].add(use_of(project))
                dependency = "direct" if info.get("type") == "Direct" else "transitive"
                previous = entry["projects"].get(project.as_posix())
                entry["projects"][project.as_posix()] = "direct" if "direct" in (previous, dependency) else "transitive"
    register = {"about": ABOUT, "packages": []}
    for (name, version) in sorted(entries, key=lambda k: (k[0].lower(), k[1])):
        entry = entries[(name, version)]
        register["packages"].append({
            "id": name, "version": version, "source": entry["source"], "contentHash": entry["contentHash"],
            "license": entry["license"], "uses": sorted(entry["uses"]),
            "projects": [{"path": p, "dependency": d} for p, d in sorted(entry["projects"].items())]})
    return register, problems


def license_allowed(expression, allowed):
    """True when some alternative of an SPDX expression uses only allowed licenses."""
    if not isinstance(expression, str):
        return False
    flat = expression.replace("(", " ").replace(")", " ")
    for alternative in re.split(r"\s+OR\s+", flat.strip()):
        terms = [t.strip() for t in re.split(r"\s+AND\s+", alternative) if t.strip()]
        if terms and all(t in allowed for t in terms):
            return True
    return False


def policy_problems(register, policy):
    allowed = set(policy.get("allowed", []))
    unrestricted = set(policy.get("unrestrictedUses", []))
    exceptions = {(e.get("id"), e.get("version"), e.get("use")) for e in policy.get("exceptions", [])}
    problems = []
    for package in register["packages"]:
        if package["source"] != NUGET_ORG:
            problems.append(f"{package['id']} {package['version']}: restored from {package['source']}, not nuget.org")
        if license_allowed(package["license"], allowed):
            continue
        for use in package["uses"]:
            if use in unrestricted:
                continue
            if (package["id"], package["version"], use) not in exceptions:
                problems.append(f"{package['id']} {package['version']}: license {json.dumps(package['license'])} "
                                f"is not allowed for {use} use and has no recorded exception")
    return problems


def render(register):
    return json.dumps(register, indent=2, ensure_ascii=False) + "\n"


def differences(old, new):
    def index(register):
        return {(p["id"], p["version"]): p for p in register.get("packages", [])}
    before, after = index(old), index(new)
    lines = []
    for key in sorted(set(before) | set(after), key=lambda k: (k[0].lower(), k[1])):
        if key not in after:
            lines.append(f"removed: {key[0]} {key[1]}")
        elif key not in before:
            lines.append(f"added: {key[0]} {key[1]}")
        elif before[key] != after[key]:
            lines.append(f"changed: {key[0]} {key[1]}")
    if not lines and old != new:
        lines.append("the register's text differs from the generated register")
    return lines


def main(argv):
    if len(argv) < 2 or argv[1] not in ("write", "check"):
        print(__doc__)
        return 2
    root = pathlib.Path(argv[2] if len(argv) > 2 else ".").resolve()
    register, problems = build(root)
    policy_path = root / POLICY
    policy = json.loads(policy_path.read_text(encoding="utf-8")) if policy_path.is_file() else {}
    if not policy_path.is_file():
        problems.append(f"{POLICY} is missing")
    problems += policy_problems(register, policy)
    register_path = root / REGISTER
    if argv[1] == "write":
        register_path.parent.mkdir(parents=True, exist_ok=True)
        register_path.write_text(render(register), encoding="utf-8")
        print(f"Wrote {REGISTER}: {len(register['packages'])} packages.")
    else:
        committed = json.loads(register_path.read_text(encoding="utf-8")) if register_path.is_file() else {}
        changes = differences(committed, register)
        if changes:
            problems += [f"{REGISTER} is out of date ({c}); run `dependency-register.py write` and review the change"
                         for c in changes]
    for problem in problems:
        print(f"dependency register: {problem}")
    if problems:
        return 1
    if argv[1] == "check":
        print(f"The dependency register matches the lock files: {len(register['packages'])} packages, every license allowed.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
