---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL object permission targets are indexed as references** — exact SQL reference search can now find objects mentioned by `GRANT`, `DENY`, and `REVOKE` statements that use `ON OBJECT::...`.

## 日本語

- **SQL object permission の target を reference として索引するようになりました** — `ON OBJECT::...` を使う `GRANT`、`DENY`、`REVOKE` statement の対象 object も、SQL の exact reference search で見つけられるようになりました。
