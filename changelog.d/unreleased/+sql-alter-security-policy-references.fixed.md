---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER SECURITY POLICY predicate tables are indexed as references** — exact SQL reference search can now find tables added to row-level security policies.

## 日本語

- **SQL ALTER SECURITY POLICY の predicate table を reference として索引するようになりました** — row-level security policy に追加される table も、SQL の exact reference search で見つけられるようになりました。
