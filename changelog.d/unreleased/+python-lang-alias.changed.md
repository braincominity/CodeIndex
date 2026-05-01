---
category: changed
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python shorthand `--lang` aliases now map to `python`** — query commands that accept `--lang` now canonicalize `py`, `py3`, `pyi`, and `pyw` to `python`, so Python-focused searches no longer miss indexed files due to alias mismatch.

## 日本語

- **Python 系の `--lang` 短縮表記を `python` に正規化するようになりました** — `--lang` を受け付けるクエリ系コマンドで、`py` / `py3` / `pyi` / `pyw` を正規名 `python` に正規化します。これにより、エイリアス不一致で Python ファイル検索が 0 件になる問題を防げます。
