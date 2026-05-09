---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL CREATE SECURITY POLICY predicate tables are indexed as references** — exact SQL reference search can now find tables mentioned after row-level security predicates.

## 日本語

- **SQL CREATE SECURITY POLICY の predicate table を reference として索引するようになりました** — row-level security predicate の後に指定される table も、SQL の exact reference search で見つけられるようになりました。
