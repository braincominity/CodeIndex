---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER PROCEDURE targets are indexed as references** — exact SQL reference search can now find procedures mentioned by `ALTER PROCEDURE` or `ALTER PROC`.

## 日本語

- **SQL ALTER PROCEDURE の target を reference として索引するようになりました** — `ALTER PROCEDURE` / `ALTER PROC` で指定される procedure も、SQL の exact reference search で見つけられるようになりました。
