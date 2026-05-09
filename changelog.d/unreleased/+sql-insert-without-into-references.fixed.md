---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL `INSERT` targets are indexed when `INTO` is omitted** — exact SQL reference search can now find write targets in SQL Server-style `INSERT dbo.Table (...)` statements.

## 日本語

- **`INTO` を省略した T-SQL `INSERT` の target を索引するようになりました** — SQL Server 形式の `INSERT dbo.Table (...)` に出る write target も、SQL の exact reference search で見つけられるようになりました。
