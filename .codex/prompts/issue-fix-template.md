# Issue Fix Prompt Template

Use this prompt for both Codex and Claude Code.

```text
Please handle the following CodeIndex issue(s):

- https://github.com/Widthdom/CodeIndex/issues/XXX
- https://github.com/Widthdom/CodeIndex/issues/YYY

Follow the repository agent guide and `.codex/workflows/issue-fix.md`.

Additional notes:
- Treat only clearly linked, duplicate, or same-root-cause issues as related.
- Do not create new issues unless they are blocking bugs or clear regressions required for this work.
- Put ordinary improvement ideas in the final `Follow-up candidates` section.
```
