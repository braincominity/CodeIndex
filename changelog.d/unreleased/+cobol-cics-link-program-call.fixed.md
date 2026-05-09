---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `LINK PROGRAM` calls are now searchable** — `EXEC CICS LINK PROGRAM(...)` lines now emit call references to linked program names.

## 日本語

- **COBOL CICS の `LINK PROGRAM` call を検索可能にしました** — `EXEC CICS LINK PROGRAM(...)` 行が link 先 program 名への call 参照を出すようになりました。
