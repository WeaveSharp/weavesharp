# Workflows

- `ci.yml` builds and tests on Linux and Windows for every pull request, and adds macOS on `main`, weekly and on demand. It first checks that every workflow action is pinned to a full commit SHA listed in `actions.allow`. Its `ci passed` job succeeds only when every build in the matrix succeeded; it and `secret scan` are the required checks.
- `secret-scan.yml` scans the full history with gitleaks, downloaded and verified against its published SHA-256, on every pull request and every push to `main`.

Workflows hold read-only permissions, never persist checkout credentials, and use only GitHub-owned actions pinned to full SHAs.
