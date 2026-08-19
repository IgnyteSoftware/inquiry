<!-- Thanks for contributing! See CONTRIBUTING.md for the full workflow. -->

## What & why

<!-- Link the issue this addresses (open one first for anything non-trivial). -->

Closes #

## Checklist

- [ ] PRs into `main`, one concern only (refactors, features, and fixes travel separately)
- [ ] Failing test written first (generator changes: emission test asserting the exact generated SQL)
- [ ] Build is warning-clean; `dotnet test` passes locally (database suites skip without Docker)
- [ ] Public API changes update `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`
- [ ] New packable projects added to `eng/release-manifest.json`
- [ ] CHANGELOG.md updated if the change is user-visible
