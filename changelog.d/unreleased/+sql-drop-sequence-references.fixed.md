---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP SEQUENCE targets are indexed as references** — exact SQL reference search can now find sequences mentioned by `DROP SEQUENCE`.

## 日本語

- **SQL DROP SEQUENCE の target を reference として索引するようになりました** — `DROP SEQUENCE` で指定される sequence も、SQL の exact reference search で見つけられるようになりました。
