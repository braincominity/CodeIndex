---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL `CREATE TRIGGER ... ON` table targets are indexed as references** — exact SQL reference search can now find tables that are only touched by trigger definitions.

## 日本語

- **SQL の `CREATE TRIGGER ... ON` table target を reference として索引するようになりました** — trigger 定義だけで触られる table も、SQL の exact reference search で見つけられるようになりました。
