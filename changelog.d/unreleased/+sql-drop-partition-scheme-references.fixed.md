---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP PARTITION SCHEME targets are indexed as references** — exact SQL reference search can now find partition schemes mentioned by `DROP PARTITION SCHEME`.

## 日本語

- **SQL DROP PARTITION SCHEME の target を reference として索引するようになりました** — `DROP PARTITION SCHEME` で指定される partition scheme も、SQL の exact reference search で見つけられるようになりました。
