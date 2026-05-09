---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **SQL `CREATE INDEX ... ON` table targets are indexed as references** — exact SQL reference search can now find tables used only by index definitions while continuing to suppress access-method names such as `btree`.

## 日本語

- **SQL の `CREATE INDEX ... ON` table target を reference として索引するようになりました** — `btree` のような access method 名は抑止したまま、index 定義だけで触られる table も SQL の exact reference search で見つけられるようになりました。
