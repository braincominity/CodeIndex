---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER TRIGGER targets are indexed as references** — exact SQL reference search can now find triggers mentioned by `ALTER TRIGGER`.

## 日本語

- **SQL ALTER TRIGGER の target を reference として索引するようになりました** — `ALTER TRIGGER` で指定される trigger も、SQL の exact reference search で見つけられるようになりました。
