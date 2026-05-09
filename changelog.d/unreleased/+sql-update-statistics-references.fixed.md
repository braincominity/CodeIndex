---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL UPDATE STATISTICS targets are indexed as references** — exact SQL reference search can now find tables mentioned by `UPDATE STATISTICS`.

## 日本語

- **SQL UPDATE STATISTICS の target を reference として索引するようになりました** — `UPDATE STATISTICS` で指定される table も、SQL の exact reference search で見つけられるようになりました。
