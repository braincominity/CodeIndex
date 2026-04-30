# Changelog Fragment Workflow

This workflow is shared by Codex, Claude Code, and other coding agents.

Use it for ordinary implementation PRs that change user-visible behavior,
CLI/MCP output, install/release behavior, documentation contracts, or agent
workflow behavior.

## Rule

Do not edit `CHANGELOG.md` in ordinary implementation PRs.
Add one or more bilingual fragments under `changelog.d/unreleased/` instead.
Treat any request to "update the changelog" as a fragment request unless the task is explicitly a release-preparation PR that aggregates existing fragments into `CHANGELOG.md`.

## Steps

1. Decide whether the change is user-visible or behavior-changing.
2. If no changelog entry is required, explain why in the PR body.
3. If required, create a fragment named `<issue>.<category>.md`,
   `<issue>-<issue>.<category>.md`, or `+<slug>.<category>.md`.
4. Use one of the allowed categories: `added`, `changed`, `fixed`,
   `deprecated`, `removed`, `security`, `docs`, `internal`.
5. Include required front matter.
6. Include both `## English` and `## 日本語` sections.
7. Do not include release headings or compare-link footer definitions.
8. Validate fragments before committing.

## Template

```md
---
category: fixed
issues:
  - 960
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **C# impact now preserves the original query string on verbatim misses (#960)** — `impact` now keeps the user-entered spelling when a C# verbatim lookup misses, so human and JSON output no longer report a misleading canonicalized query.

## 日本語

- **C# の verbatim 識別子が見つからない場合でも `impact` が元の検索文字列を維持するようになりました (#960)** — `impact` は C# verbatim lookup の miss 時にユーザー入力の綴りを保持するため、human / JSON 出力が誤解を招く canonicalized query を返さなくなりました。
```
