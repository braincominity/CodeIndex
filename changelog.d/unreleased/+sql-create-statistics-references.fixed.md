---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL CREATE STATISTICS table targets are indexed as references** — exact SQL reference search can now find tables mentioned after `CREATE STATISTICS ... ON`.

## 日本語

- **SQL CREATE STATISTICS の table target を reference として索引するようになりました** — `CREATE STATISTICS ... ON` の後に指定される table も、SQL の exact reference search で見つけられるようになりました。
