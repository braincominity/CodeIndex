---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP DEFAULT targets are indexed as references** — exact SQL reference search can now find defaults mentioned by `DROP DEFAULT`.

## 日本語

- **SQL DROP DEFAULT の target を reference として索引するようになりました** — `DROP DEFAULT` で指定される default も、SQL の exact reference search で見つけられるようになりました。
