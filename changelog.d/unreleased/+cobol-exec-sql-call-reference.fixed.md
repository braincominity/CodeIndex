---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `CALL` targets are now searchable** — `EXEC SQL CALL procedure` lines now emit call references to the stored procedure name.

## 日本語

- **COBOL embedded SQL の `CALL` target を検索可能にしました** — `EXEC SQL CALL procedure` 行が stored procedure 名への call 参照を出すようになりました。
