---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP FULLTEXT CATALOG targets are indexed as references** — exact SQL reference search can now find full-text catalogs mentioned by `DROP FULLTEXT CATALOG`.

## 日本語

- **SQL DROP FULLTEXT CATALOG の target を reference として索引するようになりました** — `DROP FULLTEXT CATALOG` で指定される full-text catalog も、SQL の exact reference search で見つけられるようになりました。
