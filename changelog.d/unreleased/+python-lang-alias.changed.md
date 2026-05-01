---
category: changed
issues: []
affected:
  - src/CodeIndex/Cli/QueryCommandRunner.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **`--lang py` now maps to `python`** — query commands that accept `--lang` now canonicalize the common Python shorthand `py`, so Python-focused searches no longer miss indexed `python` files due to alias mismatch.

## 日本語

- **`--lang py` を `python` として扱うようになりました** — `--lang` を受け付けるクエリ系コマンドで、Python の短縮表記 `py` を正規名 `python` に正規化します。これにより、エイリアス不一致で Python ファイル検索が 0 件になる問題を防げます。
