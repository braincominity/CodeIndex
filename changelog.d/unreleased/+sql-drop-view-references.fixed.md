---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP VIEW targets are indexed as references** — exact SQL reference search can now find views mentioned by `DROP VIEW`.

## 日本語

- **SQL DROP VIEW の target を reference として索引するようになりました** — `DROP VIEW` で指定される view も、SQL の exact reference search で見つけられるようになりました。
