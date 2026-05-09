---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL legacy DROP INDEX owning tables are indexed as references** — exact SQL reference search can now find tables mentioned by `DROP INDEX table.index`.

## 日本語

- **SQL legacy DROP INDEX の owning table を reference として索引するようになりました** — `DROP INDEX table.index` で指定される table も、SQL の exact reference search で見つけられるようになりました。
