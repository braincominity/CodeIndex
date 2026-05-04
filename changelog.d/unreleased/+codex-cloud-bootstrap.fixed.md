---
category: fixed
affected:
  - .agent_harness/command_guard_core.py
  - .agent_harness/tests/test_command_guard_core.py
  - .claude/hooks/bash-guard.py
  - .codex/hooks/bash_guard.py
  - .codex/README.md
  - CLOUD_BOOTSTRAP_PROMPT.md
  - DEVELOPER_GUIDE.md
  - MAINTAINERS.md
---

## English

- **Codex cloud bootstrap can use the official installer without weakening the guard** — the Codex Bash guard now permits only the official CodeIndex installer one-liner, direct repo-local `install.sh` bootstrap commands, and the documented resolver / MCP smoke commands, then requires installed `cdidx` to be invoked through the expanded absolute path while arbitrary downloads and bare/global `cdidx` remain blocked.

## 日本語

- **Codex cloud bootstrap が guard を弱めずに公式 installer を使えるようになりました** — Codex Bash guard は公式 CodeIndex installer ワンライナー、repo-local `install.sh` の直接 bootstrap 実行、ドキュメント化した resolver / MCP smoke コマンドだけを許可し、インストール後の `cdidx` は展開済み絶対パスで呼ぶ契約にしたまま、任意のダウンロードや裸・グローバル `cdidx` は引き続きブロックします。
