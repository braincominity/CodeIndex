---
category: internal
affected:
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Expanded `--lang tsql` regression coverage to callers and callees** — added CLI tests that exercise the SQL alias through `RunCallers` and `RunCallees`, so the follow-up coverage now includes the remaining graph-query entry points.

## 日本語

- **`--lang tsql` の回帰テストを callers / callees まで拡張しました** — SQL 別名を `RunCallers` と `RunCallees` で通す CLI テストを追加し、follow-up 対応のカバレッジを残りのグラフ系エントリポイントにも広げました。
