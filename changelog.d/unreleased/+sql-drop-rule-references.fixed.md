---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP RULE targets are indexed as references** — exact SQL reference search can now find rules mentioned by `DROP RULE`.

## 日本語

- **SQL DROP RULE の target を reference として索引するようになりました** — `DROP RULE` で指定される rule も、SQL の exact reference search で見つけられるようになりました。
