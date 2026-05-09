---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `CLOSE` cursors are now searchable** — `EXEC SQL CLOSE cursor` lines now emit references to the cursor name.

## 日本語

- **COBOL embedded SQL の `CLOSE` cursor を検索可能にしました** — `EXEC SQL CLOSE cursor` 行が cursor 名への参照を出すようになりました。
