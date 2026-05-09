---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DROP TYPE targets are indexed as references** — exact SQL reference search can now find user-defined types mentioned by `DROP TYPE`.

## 日本語

- **SQL DROP TYPE の target を reference として索引するようになりました** — `DROP TYPE` で指定される user-defined type も、SQL の exact reference search で見つけられるようになりました。
