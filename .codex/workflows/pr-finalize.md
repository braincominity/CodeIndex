# PR Finalization Workflow

Use this after implementation, commit, and review are complete.

## Push and PR

1. Push the branch with upstream tracking.
2. Draft the PR body as real Markdown before creating the PR.
   - Use actual newlines, blank lines, and bullet lists instead of escaped `\n` sequences in prose.
   - Prefer `--body-file` or an equivalent multiline draft when the body is not trivial.
   - Re-read the draft and confirm headings, lists, and fenced code blocks still make sense as rendered Markdown.
   - Reject any body that still contains literal control-escape text such as `\n`, `\t`, or `\r` outside code fences.
3. Create a PR against the default branch.
4. The PR title must be concise and in English.
5. The PR body must include:
   - summary;
   - validation;
   - documentation/changelog updates, or a clear rationale if none were required;
   - changelog fragment path(s) under `changelog.d/unreleased/` when ordinary changelog fragments were used;
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
