---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `PREPARE` statements are now searchable** — `EXEC SQL PREPARE statement` lines now emit references to the prepared statement name.

## 日本語

- **COBOL embedded SQL の `PREPARE` statement を検索可能にしました** — `EXEC SQL PREPARE statement` 行が prepared statement 名への参照を出すようになりました。
