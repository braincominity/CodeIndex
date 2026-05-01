---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - DEVELOPER_GUIDE.md
  - README.md
---

## English

- **T-SQL `CREATE AGGREGATE` / `ALTER AGGREGATE` declarations now surface as searchable functions** — the SQL extractor now indexes SQL Server aggregate definitions alongside the existing procedure/function/trigger rows, so `symbols`, `definition`, and related search flows can find them instead of silently skipping the declaration.

## 日本語

- **T-SQL の `CREATE AGGREGATE` / `ALTER AGGREGATE` 宣言が検索可能な function として表面化するようになりました** — SQL extractor が SQL Server の aggregate 定義を既存の procedure/function/trigger 行と同様に index するため、`symbols` / `definition` / 関連検索で宣言を取りこぼさなくなりました。
