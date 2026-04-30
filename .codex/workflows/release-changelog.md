# Release Changelog Workflow

This workflow is shared by Codex, Claude Code, and human maintainers.
Use it only for release-preparation PRs.

## Inputs

- Target version, for example `1.17.0`.
- Release date, for example `2026-05-01`.

## Start from latest main

```bash
git fetch origin
git switch -c release/v1.17.0 origin/main
```

## Prepare the changelog

```bash
dotnet run --project tools/CodeIndex.Changelog -- prepare --version 1.17.0 --date 2026-05-01
```

This command must:

- validate `changelog.d/unreleased/*.md`;
- aggregate fragments into both the English and 日本語 sections of
  `CHANGELOG.md`;
- preserve and carry forward any legacy direct content already present under
  `### [Unreleased]`;
- reset both English and 日本語 `### [Unreleased]` sections to empty;
- add `### [1.17.0] - 2026-05-01` release sections;
- update `version.json` to `1.17.0`;
- remove consumed fragments from `changelog.d/unreleased/` while keeping
  `.gitkeep`;
- update the compare-link footer.

## Compare-link footer

For a release from `1.16.0` to `1.17.0`, the footer must change from:

```md
[Unreleased]: https://github.com/Widthdom/CodeIndex/compare/v1.16.0...HEAD
[1.16.0]: https://github.com/Widthdom/CodeIndex/compare/v1.15.3...v1.16.0
```

to:

```md
[Unreleased]: https://github.com/Widthdom/CodeIndex/compare/v1.17.0...HEAD
[1.17.0]: https://github.com/Widthdom/CodeIndex/compare/v1.16.0...v1.17.0
[1.16.0]: https://github.com/Widthdom/CodeIndex/compare/v1.15.3...v1.16.0
```

Do not duplicate existing version links.

## Validate

```bash
dotnet restore CodeIndex.sln
dotnet build CodeIndex.sln -c Release
dotnet test CodeIndex.sln -c Release
dotnet pack src/CodeIndex/CodeIndex.csproj -c Release
```

## Commit and PR

```bash
git add CHANGELOG.md version.json changelog.d/unreleased tools/CodeIndex.Changelog .codex/workflows changelog.d/README.md
git commit -m "Prepare release v1.17.0"
git push -u origin release/v1.17.0
```

Create a PR titled:

```text
Prepare release v1.17.0
```

## If main moves before merge

Before merging the release PR, make sure the release branch is based on latest
`origin/main`.

```bash
git fetch origin
git switch release/v1.17.0
git merge origin/main

dotnet run --project tools/CodeIndex.Changelog -- prepare --version 1.17.0 --date 2026-05-01
```

If new fragments arrived from main, the command must add them to the existing
release section and remove those new fragments. Commit the refresh if needed.

## After the release PR merges

```bash
git switch main
git pull origin main
git tag -a v1.17.0 -m "Release v1.17.0"
git push origin v1.17.0
```
