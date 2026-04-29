# Issue Scope Policy

Use this policy when fixing GitHub issues.

## Target Issues

The target issues are the issues explicitly provided by the user.
Start with only those issues.

## Work-Started Check

Before working on a target issue, read its comments.
If another actor has clearly started work, stop work on that issue and report it.

Treat these as clear work-started signals:

- `Work started on branch ...`
- `I started working on this ...`
- any explicit claim that another person or agent is currently implementing the issue.

If the signal is ambiguous, do not silently ignore it. Mention it in the report and decide conservatively.

## Related Issues

Only include a related issue when at least one is true:

- the target issue links to it;
- it links to the target issue;
- it is clearly a duplicate;
- it has the same root cause and the same code change naturally fixes it;
- the same failing test or error message proves the relationship.

Before adding a related issue to scope:

1. Check that it does not already have a work-started comment.
2. Comment in English that work has started on the same branch.
3. Include it in the branch name if the branch has not been created yet, or mention it in the PR body if discovered later.

Do not search broadly for loosely related issues.
If more than three candidate related issues appear, do not expand scope automatically. Report them as follow-up candidates.

## New Issues

By default, do not create new issues during an issue-fix task.

Create a new issue only when all are true:

- it is a clear bug, regression, or blocking problem discovered during the current work;
- it is not merely an optional improvement;
- a quick duplicate check finds no existing issue;
- creating the issue is necessary to keep the current work accurate or auditable.

Otherwise, list the finding under `Follow-up candidates` in the final report.

## Closing Issues

If a target issue cannot be reproduced, comment with:

- reproduction steps attempted;
- environment;
- actual result;
- why it appears non-reproducible.

Close the issue only when it is clearly invalid, duplicate, already fixed, or explicitly safe to close. If unsure, leave it open and report the uncertainty.
