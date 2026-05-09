---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER PARTITION FUNCTION targets are indexed as references** — exact SQL reference search can now find partition functions mentioned by `ALTER PARTITION FUNCTION`.

## 日本語

- **SQL ALTER PARTITION FUNCTION の target を reference として索引するようになりました** — `ALTER PARTITION FUNCTION` で指定される partition function も、SQL の exact reference search で見つけられるようになりました。
