"""Refuse any workflow that could act outside the reviewed CI.

- Every `uses:` in .github/workflows names an external action at a full
  40-character commit SHA that .github/workflows/actions.allow lists exactly.
  Local actions, Docker references, tags and short hashes are refused, in
  block and flow style alike.
- Only the reviewed workflow files exist. Required checks are matched by name,
  so a workflow a change adds could otherwise report a check of the same name.
- Each required check's name appears exactly once, as the job in its own file.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
USES = re.compile(r"\buses['\"]?\s*:\s*(['\"]?)([^'\"#\s,}\]]+)\1")
PINNED = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_./-]+)?@[0-9a-f]{40}$")
# The workflow files the rulesets' required checks were reviewed against, and the check each owns.
WORKFLOWS = {"ci.yml": "ci passed", "secret-scan.yml": "secret scan"}
REQUIRED_NAME = re.compile(r"^\s*(?:-\s*)?name\s*:\s*(['\"]?)(ci passed|secret scan)\1\s*(?:#.*)?$", re.IGNORECASE)


def allowed(allow_file: pathlib.Path) -> set[str]:
    entries = set()
    for line in allow_file.read_text(encoding="utf-8").splitlines():
        entry = line.split("#", 1)[0].strip()
        if entry:
            if not PINNED.match(entry):
                sys.exit(f"actions.allow: '{entry}' is not owner/repo@<40-hex sha>")
            entries.add(entry)
    return entries


def check(root: pathlib.Path) -> tuple[list[str], int]:
    workflows = root / ".github" / "workflows"
    allow = allowed(workflows / "actions.allow")
    problems = []
    files = sorted(p for p in workflows.iterdir() if p.is_file() and p.suffix in (".yml", ".yaml"))
    for path in files:
        if path.name not in WORKFLOWS:
            problems.append(f"{path.relative_to(root)}: not a reviewed workflow; a new workflow is security work and joins "
                            "the list in .github/scripts/check-actions.py with its review")
    named = {name: [] for name in WORKFLOWS.values()}
    for path in files:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            where = f"{path.relative_to(root)}:{number}"
            required = REQUIRED_NAME.match(line)
            if required:
                named[required.group(2).lower()].append((path.name, where))
            for match in USES.finditer(line.split(" #", 1)[0]):
                reference = match.group(2)
                if reference.startswith(("./", "docker://")):
                    problems.append(f"{where}: '{reference}' is a local or container action")
                elif not PINNED.match(reference):
                    problems.append(f"{where}: '{reference}' is not pinned to a full commit SHA")
                elif reference not in allow:
                    problems.append(f"{where}: '{reference}' is not in actions.allow")
    for owner, check_name in WORKFLOWS.items():
        places = named[check_name]
        if [name for name, _ in places] != [owner]:
            listed = ", ".join(where for _, where in places) or "nowhere"
            problems.append(f"the required check '{check_name}' must be named exactly once, as a job in {owner}; found at {listed}")
    return problems, len(files)


def main() -> int:
    problems, count = check(ROOT)
    for problem in problems:
        print(problem, file=sys.stderr)
    if not problems:
        print(f"{count} workflow files: every action is pinned and allow-listed, and each required check has one owner.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
