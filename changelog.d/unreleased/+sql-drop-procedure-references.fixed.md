---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP PROCEDURE targets are indexed as references** — exact SQL reference search can now find procedures mentioned by `DROP PROCEDURE` or `DROP PROC`.

## 日本語

- **SQL DROP PROCEDURE の target を reference として索引するようになりました** — `DROP PROCEDURE` / `DROP PROC` で指定される procedure も、SQL の exact reference search で見つけられるようになりました。
