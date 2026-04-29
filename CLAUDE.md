# Claude Code Entry Point

Read `AGENT_GUIDE.md` first. It is the shared source of truth for CodeIndex agent behavior.

For task-specific procedures, read the relevant workflow in `.codex/workflows/`:

- issue fixing: `.codex/workflows/issue-fix.md`
- adversarial review: `.codex/workflows/adversarial-review.md`
- commit checks: `.codex/workflows/precommit.md`
- PR finalization and CI checks: `.codex/workflows/pr-finalize.md`
- related/new issue scope control: `.codex/workflows/issue-scope.md`

The `.codex/workflows/` directory is a shared workflow library for all coding agents, not only Codex.

## Claude Code Notes

- Follow the repository-tracked `.claude/settings.json` and `.claude/hooks/bash-guard.py` policy files when running in Claude Code.
- Do not edit those policy files during ordinary implementation work unless the task is explicitly about Claude Code guard behavior.
- For shell search and navigation, prefer the built-in Grep / Glob tools or the locally built `cdidx` binary described in `AGENT_GUIDE.md`.
