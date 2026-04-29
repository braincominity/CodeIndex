# PR Finalization Workflow

Use this after implementation, commit, and review are complete.

## Push and PR

1. Push the branch with upstream tracking.
2. Create a PR against the default branch.
3. The PR title must be concise and in English.
4. The PR body must include:
   - summary;
   - validation;
   - `Fixes #...` lines for issues that should auto-close;
   - follow-up candidates, if any.

## Conflicts

After creating the PR, check whether it has merge conflicts.
If conflicts exist and can be resolved in the current environment, resolve them and push.
If conflicts cannot be resolved safely, report the conflicting files and recommended next action.

## CI Checks

Check CI status after PR creation.
If CI has already failed, inspect the failure, fix it if possible, commit, and push.

If CI is pending, check at most three times.
Do not wait indefinitely.
If CI is still pending after the bounded checks, report:

- PR link;
- current CI state;
- command the user can run to check status;
- any known risks.

## Final Report

Report:

- PR link;
- branch name;
- issues covered;
- commits created;
- validation performed;
- CI/conflict status;
- follow-up candidates.
