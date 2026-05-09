---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL synonym base objects are indexed as references** — exact SQL reference search can now find objects that are only mentioned after `CREATE SYNONYM ... FOR`.

## 日本語

- **SQL synonym の base object を reference として索引するようになりました** — `CREATE SYNONYM ... FOR` の後にだけ現れる object も、SQL の exact reference search で見つけられるようになりました。
