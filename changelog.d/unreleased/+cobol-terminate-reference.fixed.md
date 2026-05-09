---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL `TERMINATE` statements are now searchable** — `TERMINATE report-name` lines now emit a reference to the report writer target.

## 日本語

- **COBOL の `TERMINATE` 文を検索可能にしました** — `TERMINATE report-name` 行が Report Writer の対象 report への参照を出すようになりました。
