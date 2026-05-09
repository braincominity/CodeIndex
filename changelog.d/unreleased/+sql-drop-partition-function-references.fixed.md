---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP PARTITION FUNCTION targets are indexed as references** — exact SQL reference search can now find partition functions mentioned by `DROP PARTITION FUNCTION`.

## 日本語

- **SQL DROP PARTITION FUNCTION の target を reference として索引するようになりました** — `DROP PARTITION FUNCTION` で指定される partition function も、SQL の exact reference search で見つけられるようになりました。
