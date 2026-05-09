---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `EXECUTE` statements are now searchable** — `EXEC SQL EXECUTE statement` lines now emit references to the prepared statement name.

## 日本語

- **COBOL embedded SQL の `EXECUTE` statement を検索可能にしました** — `EXEC SQL EXECUTE statement` 行が prepared statement 名への参照を出すようになりました。
