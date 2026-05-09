---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `XCTL PROGRAM` transfers are now searchable** — `EXEC CICS XCTL PROGRAM(...)` lines now emit call references to target program names.

## 日本語

- **COBOL CICS の `XCTL PROGRAM` transfer を検索可能にしました** — `EXEC CICS XCTL PROGRAM(...)` 行が遷移先 program 名への call 参照を出すようになりました。
