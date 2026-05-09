---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `START TRANSID` targets are now searchable** — `EXEC CICS START TRANSID(...)` lines now emit references to transaction ids.

## 日本語

- **COBOL CICS の `START TRANSID` target を検索可能にしました** — `EXEC CICS START TRANSID(...)` 行が transaction id への参照を出すようになりました。
