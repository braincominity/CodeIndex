---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP FULLTEXT INDEX table targets are indexed as references** — exact SQL reference search can now find tables mentioned after `DROP FULLTEXT INDEX ON`.

## 日本語

- **SQL DROP FULLTEXT INDEX の table target を reference として索引するようになりました** — `DROP FULLTEXT INDEX ON` の後に指定される table も、SQL の exact reference search で見つけられるようになりました。
