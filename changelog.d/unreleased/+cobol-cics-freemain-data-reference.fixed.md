---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `FREEMAIN DATA` data areas are now searchable** — `EXEC CICS FREEMAIN DATA(...)` lines now emit references to freed data areas.

## 日本語

- **COBOL CICS の `FREEMAIN DATA` data area を検索可能にしました** — `EXEC CICS FREEMAIN DATA(...)` 行が解放対象 data area への参照を出すようになりました。
