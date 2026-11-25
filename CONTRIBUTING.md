# Contributing

## Tests and CI

Tests run locally only. GitHub Actions never builds or tests the code, to keep the Actions quota untouched.

### Local tests

Run the tests scoped to the touched projects before each commit:

```powershell
dotnet format KeyStats.slnx --verify-no-changes
dotnet build KeyStats.slnx -warnaserror
dotnet test --project tests/KeyStats.Core.Tests --filter "FullyQualifiedName~KeyStats.Core.Tests.<Area>"
```

The filter narrows the run to the test classes of the changed area. A change to build configuration, `Directory.*.props`, the storage schema or test tooling calls for the full `dotnet test --project tests/KeyStats.Core.Tests`.

The PR body carries a `## Tests` section listing the commands or categories run locally and their result.

### What `pr-gate` checks

`.github/workflows/pr-gate.yml` runs on every pull request in a few seconds, without restore, build or tests:

- **Size cap**: at most 400 changed lines against the base branch, unless the PR carries the `large-pr` label (exact name) with a written justification in the body.
- **Tests section**: the PR body, read from the API, contains a non-empty `## Tests` section. Editing the body re-runs the check.

The repository ruleset on `main` requires the `pr-gate` check before merging.

### Rollback

To restore the former CI, change the ruleset first, then the workflow:

1. In the `main` ruleset, replace the required `pr-gate` check with the checks of the restored workflow (or remove the requirement). Otherwise every open PR stays blocked on a check that no longer reports.
2. Restore `.github/workflows/ci.yml` from history and delete `pr-gate.yml` in a PR.
