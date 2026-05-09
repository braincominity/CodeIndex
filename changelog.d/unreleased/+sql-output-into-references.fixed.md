---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL OUTPUT INTO targets are indexed as references** — exact SQL reference search can now find audit or capture tables mentioned by DML `OUTPUT ... INTO`.

## 日本語

- **SQL OUTPUT INTO の target を reference として索引するようになりました** — DML の `OUTPUT ... INTO` で指定される audit/capture table も、SQL の exact reference search で見つけられるようになりました。
