# Changelog Fragments

Changelog fragments exist to avoid merge conflicts in the shared `CHANGELOG.md`
`[Unreleased]` sections when multiple issue-fix branches land in parallel.
Instead of editing `CHANGELOG.md` directly, ordinary implementation PRs add one
small bilingual fragment under `changelog.d/unreleased/`.

## Why this exists

- avoid `CHANGELOG.md` conflicts across parallel worktrees and PRs;
- keep release prep as a single aggregation step;
- make the changelog contract explicit for both human maintainers and coding
  agents.

## Normal PR rule

For ordinary issue-fix or feature PRs:

- do not edit `CHANGELOG.md`;
- add a bilingual fragment under `changelog.d/unreleased/` when the change is
  user-visible or behavior-changing;
- include both `## English` and `## Japanese` sections;
- keep the fragment small and focused.

## File names

Use one of these forms:

- `<issue-number>.<category>.md`
- `<issue-number>-<issue-number>.<category>.md`
- `+<slug>.<category>.md`

Examples:

- `195.fixed.md`
- `344-484.fixed.md`
- `+agent-workflow.changed.md`
- `+release-process.docs.md`

Use issue numbers when the PR is tied to GitHub issues. Use `+<slug>` for
workflow, infrastructure, documentation, or other changes without a stable
issue number.

## Categories

Allowed categories:

- `added`
- `changed`
- `fixed`
- `deprecated`
- `removed`
- `security`
- `docs`
- `internal`

The release tool maps them to `CHANGELOG.md` headings as follows:

| Fragment category | English heading | Japanese heading |
|---|---|---|
| `added` | `Added` | `追加` |
| `changed` | `Changed` | `変更` |
| `fixed` | `Fixed` | `修正` |
| `deprecated` | `Deprecated` | `非推奨` |
| `removed` | `Removed` | `削除` |
| `security` | `Security` | `セキュリティ` |
| `docs` | `Documentation` | `ドキュメント` |
| `internal` | `Internal` | `内部変更` |

`internal` fragments are included only when the fragment explicitly exists.

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

## Japanese

- **C# の verbatim 識別子が見つからない場合でも `impact` が元の検索文字列を維持するようになりました (#960)** — `impact` は C# verbatim lookup の miss 時にユーザー入力の綴りを保持するため、human / JSON 出力が誤解を招く canonicalized query を返さなくなりました。
```

## Release preparation

Release preparation is the only normal path that aggregates fragments into
`CHANGELOG.md`. The release workflow:

- validates all fragment files;
- preserves any legacy direct `[Unreleased]` content already present in
  `CHANGELOG.md`;
- carries fragments into the new release section;
- resets `[Unreleased]`;
- updates `version.json`;
- removes consumed fragment files;
- updates the compare-link footer.

Use `.codex/workflows/release-changelog.md` for the command sequence.

## Compare-link footer

Release prep updates the bottom footer so `CHANGELOG.md` stays on the latest
unreleased comparison target:

- `[Unreleased]` should compare from the current release version to `HEAD`;
- the new release tag should compare from the previous version to the new tag;
- older version links stay intact and are not duplicated.

## Examples

- `+release-process.docs.md`
- `+agent-workflow.changed.md`
- `195.fixed.md`
- `344-484.fixed.md`

If you are unsure, prefer a short `+slug` file name for repository workflow,
docs, or infrastructure work that does not correspond to a stable issue number.
