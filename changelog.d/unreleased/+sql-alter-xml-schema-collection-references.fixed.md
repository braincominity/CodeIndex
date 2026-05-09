---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER XML SCHEMA COLLECTION targets are indexed as references** — exact SQL reference search can now find XML schema collections mentioned by `ALTER XML SCHEMA COLLECTION`.

## 日本語

- **SQL ALTER XML SCHEMA COLLECTION の target を reference として索引するようになりました** — `ALTER XML SCHEMA COLLECTION` で指定される XML schema collection も、SQL の exact reference search で見つけられるようになりました。
