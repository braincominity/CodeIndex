---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER SECURITY POLICY names are indexed as references** — exact SQL reference search can now find security policies mentioned by `ALTER SECURITY POLICY`.

## 日本語

- **SQL ALTER SECURITY POLICY の policy name を reference として索引するようになりました** — `ALTER SECURITY POLICY` で指定される security policy も、SQL の exact reference search で見つけられるようになりました。
