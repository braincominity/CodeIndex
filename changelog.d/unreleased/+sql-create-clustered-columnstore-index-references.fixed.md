---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL CREATE CLUSTERED COLUMNSTORE INDEX table targets are indexed as references** — exact SQL reference search can now find tables mentioned by clustered columnstore index creation.

## 日本語

- **SQL CREATE CLUSTERED COLUMNSTORE INDEX の table target を reference として索引するようになりました** — clustered columnstore index 作成で指定される table も、SQL の exact reference search で見つけられるようになりました。
