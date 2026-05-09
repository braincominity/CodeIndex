---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL system-versioning history tables are indexed as references** — exact SQL reference search can now find tables mentioned by `HISTORY_TABLE = ...`.

## 日本語

- **SQL system-versioning の history table を reference として索引するようになりました** — `HISTORY_TABLE = ...` で指定される table も、SQL の exact reference search で見つけられるようになりました。
