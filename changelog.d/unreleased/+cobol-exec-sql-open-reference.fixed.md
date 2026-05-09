---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `OPEN` cursors are now searchable** — `EXEC SQL OPEN cursor` lines now emit references to the cursor name.

## 日本語

- **COBOL embedded SQL の `OPEN` cursor を検索可能にしました** — `EXEC SQL OPEN cursor` 行が cursor 名への参照を出すようになりました。
