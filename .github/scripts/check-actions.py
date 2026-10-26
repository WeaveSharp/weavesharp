"""Refuse any workflow action reference that is not pinned and allow-listed.

Every `uses:` in .github/workflows must name an external action at a full
40-character commit SHA that .github/workflows/actions.allow lists exactly.
Local actions, Docker references, tags and short hashes are refused.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
ALLOW = WORKFLOWS / "actions.allow"
USES = re.compile(r"^\s*(?:-\s*)?uses:\s*(['\"]?)([^'\"#\s]+)\1")
PINNED = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_./-]+)?@[0-9a-f]{40}$")


def allowed() -> set[str]:
    entries = set()
    for line in ALLOW.read_text(encoding="utf-8").splitlines():
        entry = line.split("#", 1)[0].strip()
        if entry:
            if not PINNED.match(entry):
                sys.exit(f"actions.allow: '{entry}' is not owner/repo@<40-hex sha>")
            entries.add(entry)
    return entries


def main() -> int:
    allow = allowed()
    problems = []
    files = sorted(WORKFLOWS.glob("*.yml")) + sorted(WORKFLOWS.glob("*.yaml"))
    for path in files:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = USES.match(line)
            if not match:
                continue
            reference = match.group(2)
            where = f"{path.relative_to(ROOT)}:{number}"
            if reference.startswith(("./", "docker://")):
                problems.append(f"{where}: '{reference}' is a local or container action")
            elif not PINNED.match(reference):
                problems.append(f"{where}: '{reference}' is not pinned to a full commit SHA")
            elif reference not in allow:
                problems.append(f"{where}: '{reference}' is not in actions.allow")
    for problem in problems:
        print(problem, file=sys.stderr)
    if not problems:
        print(f"{len(files)} workflow files: every action is pinned and allow-listed.")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
