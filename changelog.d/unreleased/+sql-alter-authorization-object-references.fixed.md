---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER AUTHORIZATION object targets are indexed as references** — exact SQL reference search can now find objects mentioned by `ALTER AUTHORIZATION ON OBJECT::...`.

## 日本語

- **SQL ALTER AUTHORIZATION の object target を reference として索引するようになりました** — `ALTER AUTHORIZATION ON OBJECT::...` で指定される object も、SQL の exact reference search で見つけられるようになりました。
