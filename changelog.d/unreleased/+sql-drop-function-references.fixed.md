---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP FUNCTION targets are indexed as references** — exact SQL reference search can now find functions mentioned by `DROP FUNCTION`.

## 日本語

- **SQL DROP FUNCTION の target を reference として索引するようになりました** — `DROP FUNCTION` で指定される function も、SQL の exact reference search で見つけられるようになりました。
