---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER FUNCTION targets are indexed as references** — exact SQL reference search can now find functions mentioned by `ALTER FUNCTION`.

## 日本語

- **SQL ALTER FUNCTION の target を reference として索引するようになりました** — `ALTER FUNCTION` で指定される function も、SQL の exact reference search で見つけられるようになりました。
