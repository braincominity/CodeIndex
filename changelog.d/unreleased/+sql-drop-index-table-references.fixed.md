---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL `DROP INDEX ... ON` table targets are indexed as references** — exact SQL reference search can now find tables touched only by SQL Server index cleanup scripts.

## 日本語

- **T-SQL の `DROP INDEX ... ON` table target を reference として索引するようになりました** — SQL Server の index cleanup script だけで触られる table も、SQL の exact reference search で見つけられるようになりました。
