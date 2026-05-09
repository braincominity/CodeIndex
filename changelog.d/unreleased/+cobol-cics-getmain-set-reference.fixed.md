---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `GETMAIN SET` data areas are now searchable** — `EXEC CICS GETMAIN SET(...)` lines now emit references to pointer data areas.

## 日本語

- **COBOL CICS の `GETMAIN SET` data area を検索可能にしました** — `EXEC CICS GETMAIN SET(...)` 行が pointer data area への参照を出すようになりました。
