---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER FULLTEXT INDEX table targets are indexed as references** — exact SQL reference search can now find tables mentioned after `ALTER FULLTEXT INDEX ON`.

## 日本語

- **SQL ALTER FULLTEXT INDEX の table target を reference として索引するようになりました** — `ALTER FULLTEXT INDEX ON` の後に指定される table も、SQL の exact reference search で見つけられるようになりました。
