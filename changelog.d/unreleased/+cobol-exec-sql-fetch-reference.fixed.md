---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `FETCH` cursors are now searchable** — `EXEC SQL FETCH cursor` lines now emit references to the cursor name.

## 日本語

- **COBOL embedded SQL の `FETCH` cursor を検索可能にしました** — `EXEC SQL FETCH cursor` 行が cursor 名への参照を出すようになりました。
