---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER VIEW targets are indexed as references** — exact SQL reference search can now find views mentioned by `ALTER VIEW`.

## 日本語

- **SQL ALTER VIEW の target を reference として索引するようになりました** — `ALTER VIEW` で指定される view も、SQL の exact reference search で見つけられるようになりました。
