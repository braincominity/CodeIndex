# Codex Entry Point

Read `AGENT_GUIDE.md` first. It is the shared source of truth for CodeIndex agent behavior.

For task-specific procedures, read the relevant workflow in `.codex/workflows/`:

- issue fixing: `.codex/workflows/issue-fix.md`
- changelog fragments: `.codex/workflows/changelog-fragment.md`
- release changelog: `.codex/workflows/release-changelog.md`
- adversarial review: `.codex/workflows/adversarial-review.md`
- commit checks: `.codex/workflows/precommit.md`
- PR finalization and CI checks: `.codex/workflows/pr-finalize.md`
- related/new issue scope control: `.codex/workflows/issue-scope.md`

Do not duplicate workflow rules in this file. Update the shared files instead.

## Code search and safety policy

For CodeIndex work, dogfood the project-built CodeIndex binary.

Do not use grep, rg, ripgrep, ag, ack, find, fd, locate, git grep, Python scripts,
or a globally installed cdidx for code search or repository discovery.

Use:

`dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll`

Examples:

- `dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll search SymbolExtractor`
- `dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll symbols --lang csharp`
- `dotnet ./src/CodeIndex/bin/Debug/net8.0/cdidx.dll inspect src/CodeIndex/Indexer/SymbolExtractor.cs`

This instruction is a workflow rule. Enforcement is provided separately by the
Claude and Codex guard hooks.
