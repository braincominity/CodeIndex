---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/SqlNameResolver.cs
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP STATISTICS owning tables are indexed as references** — exact SQL reference search can now find tables mentioned by `DROP STATISTICS table.statistic`.

## 日本語

- **SQL DROP STATISTICS の owning table を reference として索引するようになりました** — `DROP STATISTICS table.statistic` で指定される table も、SQL の exact reference search で見つけられるようになりました。
