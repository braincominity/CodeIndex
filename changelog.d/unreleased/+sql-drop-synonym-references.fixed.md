---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP SYNONYM targets are indexed as references** — exact SQL reference search can now find synonyms mentioned by `DROP SYNONYM`.

## 日本語

- **SQL DROP SYNONYM の target を reference として索引するようになりました** — `DROP SYNONYM` で指定される synonym も、SQL の exact reference search で見つけられるようになりました。
