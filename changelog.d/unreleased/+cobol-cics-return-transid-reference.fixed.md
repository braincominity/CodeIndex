---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL CICS `RETURN TRANSID` targets are now searchable** — `EXEC CICS RETURN TRANSID(...)` lines now emit references to transaction ids.

## 日本語

- **COBOL CICS の `RETURN TRANSID` target を検索可能にしました** — `EXEC CICS RETURN TRANSID(...)` 行が transaction id への参照を出すようになりました。
