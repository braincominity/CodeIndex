---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - src/CodeIndex/Database/DbSymbolReader.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
  - README.md
---

## English

- **`--lang tsql` now resolves to the SQL index bucket** — query-time language filters treat `tsql` as an alias for `sql`, so T-SQL projects get the same search, definition, caller, and reference results as the SQL bucket.

## 日本語

- **`--lang tsql` が SQL インデックスバケットに解決されるようになりました** — クエリ時の言語フィルタで `tsql` を `sql` の別名として扱うため、T-SQL プロジェクトでも SQL バケットと同じ検索・定義・caller・reference 結果を得られます。
