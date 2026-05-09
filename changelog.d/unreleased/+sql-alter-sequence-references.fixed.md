---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER SEQUENCE targets are indexed as references** — exact SQL reference search can now find sequences mentioned by `ALTER SEQUENCE`.

## 日本語

- **SQL ALTER SEQUENCE の target を reference として索引するようになりました** — `ALTER SEQUENCE` で指定される sequence も、SQL の exact reference search で見つけられるようになりました。
