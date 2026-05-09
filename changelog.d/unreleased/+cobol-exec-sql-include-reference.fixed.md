---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL embedded SQL `INCLUDE` targets are now searchable** — `EXEC SQL INCLUDE name` lines now emit references to included copybooks or SQL declarations.

## 日本語

- **COBOL embedded SQL の `INCLUDE` target を検索可能にしました** — `EXEC SQL INCLUDE name` 行が include 対象の copybook / SQL declaration への参照を出すようになりました。
