---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER SCHEMA transfer targets are indexed as references** — exact SQL reference search can now find objects moved by `ALTER SCHEMA ... TRANSFER`.

## 日本語

- **SQL ALTER SCHEMA の transfer target を reference として索引するようになりました** — `ALTER SCHEMA ... TRANSFER` で移動される object も、SQL の exact reference search で見つけられるようになりました。
