# Adversarial Review Workflow

Use this workflow to review `origin/main..HEAD`.

## Scope

Review only the diff from `origin/main` to current `HEAD` unless the user specifies another range.

## Search Rules

For code search, do not use `grep`, `rg`, `python`, or a globally installed `cdidx`.
Use the locally built binary:

```bash
dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll
```

## Focus

Report only blocking or actionable problems, such as:

- bugs;
- specification violations;
- regressions;
- missing tests for changed behavior;
- documentation inconsistencies that would mislead users;
- commit checklist violations;
- CI/build/test risks that are likely to fail.

Do not report nitpicks, style preferences, or speculative concerns unless they create real risk.

## Output Language

Output review results in English unless the user requests otherwise.

## Required Output Format

If problems are found, include each finding with:

- severity;
- affected file or area;
- why it matters;
- how to fix it;
- possible implementation example, when useful.

If no blocking or actionable problems are found, say exactly:

```text
No blocking/actionable issues found.
```

## Review Discipline

Do not drip-feed findings. Try to report all findings in one pass.
If reviewing after a fix, focus on the new diff and previously reported findings.
