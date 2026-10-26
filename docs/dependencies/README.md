# Dependencies

WeaveSharp takes as few dependencies as it can, and every one it takes is locked, comes from a known source, is recorded and is licensed for how WeaveSharp uses it. CI enforces each rule below on every pull request.

## The rules

- **One source.** `NuGet.Config` clears every inherited source, uses nuget.org alone, and maps every package id to it, so a source configured on a developer's machine or a CI runner can never supply a package. No other NuGet configuration file may exist in the repository.
- **Exact, central versions.** Versions live only in `Directory.Packages.props`, each one exact. A project's `PackageReference` names the package without a version, and `VersionOverride` and `PackageDownload` are not used.
- **Lock files.** Every project restores with a lock file, and its `packages.lock.json` is committed. CI restores in locked mode, refuses a restore that changes a lock file or creates one that isn't committed, and every restore in CI is locked, whatever command triggers it.
- **Advisories.** NuGet audits direct and transitive packages on every restore. Because warnings are errors, a known advisory stops the build until the package is updated, or the advisory is suppressed with `NuGetAuditSuppress` and a recorded reason.
- **Licenses.** Every package's license must be in `license-policy.json`'s allowed list, or have an exception there naming the package, its version, its use, the reason and the decision that allows it. A license for code that ships in an extension is the operator's decision. Packages used only under `experiments/`, which ships nothing, have their licenses recorded but not restricted (`unrestrictedUses`); code moves from `experiments/` into the product only through SEC-05's license review.
- **npm, when it arrives.** `.npmrc` turns install scripts off and saves exact versions. Every `package.json` has a committed `package-lock.json`, workflows install with `npm ci --ignore-scripts` only, and npm is the only JavaScript package manager.

## The register

`register.json` lists every NuGet package any project restores, direct or transitive. For each one it gives:

- its id and exact version;
- the source it came from;
- the content hash its lock file pins;
- its license;
- each project that uses it, directly or transitively;
- how WeaveSharp uses it: `test` for `tests/`, `experiment` for `experiments/`, and `product` for everything else.

`.github/scripts/dependency-register.py` generates the register from the committed lock files and the restored packages' own metadata, and CI checks that it matches. Never edit it by hand.

## Adding or changing a dependency

1. Add or change its exact version in `Directory.Packages.props`, and reference it without a version.
2. Restore (`dotnet restore`) so the affected lock files update, and commit them.
3. Regenerate the register: `python3 .github/scripts/dependency-register.py write`.
4. Read the register's diff: every new package, direct or transitive, its source and its license. If a license isn't allowed, don't add an exception yourself; ask for a decision.
5. Run `python3 .github/scripts/check-supply-chain.py` and `python3 .github/scripts/dependency-register.py check`, and open the pull request.

Supply-chain changes are security work: the Builder reviews every dependency change, and the Reviewer reviews the rules before a release that carries them.
