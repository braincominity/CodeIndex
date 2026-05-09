---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER PARTITION SCHEME targets are indexed as references** — exact SQL reference search can now find partition schemes mentioned by `ALTER PARTITION SCHEME`.

## 日本語

- **SQL ALTER PARTITION SCHEME の target を reference として索引するようになりました** — `ALTER PARTITION SCHEME` で指定される partition scheme も、SQL の exact reference search で見つけられるようになりました。
