# Agent Workflows

This directory contains shared task workflows for CodeIndex coding agents.
Despite the `.codex` directory name, these workflows are used by both Codex and Claude Code.

Use these workflows instead of pasting long repeated instructions into every prompt.
Start from the thin entry point for your tool (`AGENTS.md` for Codex or `CLAUDE.md` for Claude Code), then follow `AGENT_GUIDE.md` to the relevant workflow here.

## Workflows

- `issue-fix.md` - fix one or more GitHub issues.
- `changelog-fragment.md` - add bilingual changelog fragments in ordinary PRs.
- `release-changelog.md` - aggregate fragments into `CHANGELOG.md` for a release.
- `adversarial-review.md` - review `origin/main..HEAD` for blocking/actionable problems.
- `precommit.md` - checks to perform before each commit.
- `issue-scope.md` - rules for related issues, new issues, and scope expansion.
- `pr-finalize.md` - push, PR creation, conflict checks, and bounded CI checks.

## Rule Placement

- Shared rules go in `AGENT_GUIDE.md`.
- Task procedures go in this directory.
- Codex-specific entry instructions go in `AGENTS.md`.
- Claude-specific entry instructions go in `CLAUDE.md`.
- Do not duplicate full workflows across entry-point files.
