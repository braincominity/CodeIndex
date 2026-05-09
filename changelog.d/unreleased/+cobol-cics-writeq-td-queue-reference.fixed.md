---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `WRITEQ TD QUEUE` targets are now searchable** — `EXEC CICS WRITEQ TD QUEUE(...)` lines now emit references to transient data queue names.

## 日本語

- **COBOL CICS の `WRITEQ TD QUEUE` target を検索可能にしました** — `EXEC CICS WRITEQ TD QUEUE(...)` 行が transient data queue 名への参照を出すようになりました。
