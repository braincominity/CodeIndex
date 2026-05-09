---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL bare object permission targets are indexed as references** — exact SQL reference search can now find objects mentioned by `GRANT`, `DENY`, and `REVOKE` statements that use `ON schema.object`.

## 日本語

- **SQL bare object permission の target を reference として索引するようになりました** — `ON schema.object` を使う `GRANT`、`DENY`、`REVOKE` statement の対象 object も、SQL の exact reference search で見つけられるようになりました。
