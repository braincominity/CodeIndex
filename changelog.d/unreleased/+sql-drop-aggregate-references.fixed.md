---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP AGGREGATE targets are indexed as references** — exact SQL reference search can now find aggregates mentioned by `DROP AGGREGATE`.

## 日本語

- **SQL DROP AGGREGATE の target を reference として索引するようになりました** — `DROP AGGREGATE` で指定される aggregate も、SQL の exact reference search で見つけられるようになりました。
