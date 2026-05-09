---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL `ALTER INDEX ... ON` table targets are indexed as references** — exact SQL reference search can now find tables touched only by index rebuild or reorganize maintenance scripts.

## 日本語

- **T-SQL の `ALTER INDEX ... ON` table target を reference として索引するようになりました** — index rebuild / reorganize の maintenance script だけで触られる table も、SQL の exact reference search で見つけられるようになりました。
