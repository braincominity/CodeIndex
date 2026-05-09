---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL `INITIATE` statements are now searchable** — `INITIATE report-name` lines now emit a reference to the report writer target.

## 日本語

- **COBOL の `INITIATE` 文を検索可能にしました** — `INITIATE report-name` 行が Report Writer の対象 report への参照を出すようになりました。
