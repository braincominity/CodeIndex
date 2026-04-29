# Precommit Workflow

Run this before each commit.

## Checklist

1. Confirm the change is scoped to the requested issue or task.
2. Confirm no unrelated refactors were added.
3. Confirm code search followed the local `cdidx` rule from `AGENT_GUIDE.md`.
4. Run the relevant build and test commands for the files changed.
5. If public behavior changed, confirm tests were added or updated.
6. If documentation changed, confirm it matches implementation.
7. If changelog content changed, confirm both English and Japanese entries are present where required by repository convention.
8. Confirm issue auto-close references will be placed in the PR body as `Fixes #...`.
9. Confirm the commit message is English and includes relevant issue numbers.
10. Check `git diff --stat` and `git diff` for accidental changes.

## Validation Commands

Use the repository's documented commands. If no narrower command is clearly sufficient, use:

```bash
dotnet restore CodeIndex.sln
dotnet build CodeIndex.sln -c Release
dotnet test CodeIndex.sln -c Release
```

If a command cannot run in the current environment, report the reason and the exact command the user should run.
