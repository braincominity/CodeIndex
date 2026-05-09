---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP ASSEMBLY targets are indexed as references** — exact SQL reference search can now find assemblies mentioned by `DROP ASSEMBLY`.

## 日本語

- **SQL DROP ASSEMBLY の target を reference として索引するようになりました** — `DROP ASSEMBLY` で指定される assembly も、SQL の exact reference search で見つけられるようになりました。
