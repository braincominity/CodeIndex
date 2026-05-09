---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL CREATE PRIMARY/SELECTIVE XML INDEX table targets are indexed as references** — exact SQL reference search can now find tables mentioned after special XML index creation.

## 日本語

- **SQL CREATE PRIMARY/SELECTIVE XML INDEX の table target を reference として索引するようになりました** — special XML index 作成で指定される table も、SQL の exact reference search で見つけられるようになりました。
