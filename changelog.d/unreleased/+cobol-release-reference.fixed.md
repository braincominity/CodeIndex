---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL `RELEASE` statements are now searchable** — `RELEASE record-name` lines now emit a reference to the released sort or merge record.

## 日本語

- **COBOL の `RELEASE` 文を検索可能にしました** — `RELEASE record-name` 行が release 対象の sort / merge record への参照を出すようになりました。
