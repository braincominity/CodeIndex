---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL `SELECT ... INTO` table targets are indexed beyond temp tables** — exact SQL reference search can now find non-temp tables created or written through `SELECT ... INTO dbo.Table`.

## 日本語

- **T-SQL の `SELECT ... INTO` table target を temp table 以外でも索引するようになりました** — `SELECT ... INTO dbo.Table` で作成または書き込まれる非 temp table も、SQL の exact reference search で見つけられるようになりました。
