---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL DELETE targets without FROM are indexed as references** — exact SQL reference search can now find qualified tables mentioned by `DELETE schema.table`.

## 日本語

- **SQL DELETE の FROM なし target を reference として索引するようになりました** — `DELETE schema.table` で指定される qualified table も、SQL の exact reference search で見つけられるようになりました。
