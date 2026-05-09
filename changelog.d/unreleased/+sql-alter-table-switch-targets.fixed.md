---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL ALTER TABLE SWITCH targets are indexed as references** — exact SQL reference search can now find destination tables mentioned after `SWITCH TO`.

## 日本語

- **SQL ALTER TABLE SWITCH の destination table を reference として索引するようになりました** — `SWITCH TO` の後に指定される table も、SQL の exact reference search で見つけられるようになりました。
