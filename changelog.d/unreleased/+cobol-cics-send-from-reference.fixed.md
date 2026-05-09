---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `SEND FROM` data areas are now searchable** — `EXEC CICS SEND FROM(...)` lines now emit references to send buffers.

## 日本語

- **COBOL CICS の `SEND FROM` data area を検索可能にしました** — `EXEC CICS SEND FROM(...)` 行が send buffer への参照を出すようになりました。
