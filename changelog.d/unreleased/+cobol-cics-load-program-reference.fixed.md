---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `LOAD PROGRAM` targets are now searchable** — `EXEC CICS LOAD PROGRAM(...)` lines now emit references to loaded program names.

## 日本語

- **COBOL CICS の `LOAD PROGRAM` target を検索可能にしました** — `EXEC CICS LOAD PROGRAM(...)` 行が load 対象 program 名への参照を出すようになりました。
