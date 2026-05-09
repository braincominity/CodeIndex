---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL `BULK INSERT` destinations are indexed as table references** — exact SQL reference search can now find tables loaded through `BULK INSERT dbo.Table FROM ...`.

## 日本語

- **T-SQL の `BULK INSERT` destination を table reference として索引するようになりました** — `BULK INSERT dbo.Table FROM ...` でロードされる table も、SQL の exact reference search で見つけられるようになりました。
