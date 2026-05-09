---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL `RETURN` statements are now searchable** — `RETURN sort-file` lines now emit a reference to the returned sort or merge file.

## 日本語

- **COBOL の `RETURN` 文を検索可能にしました** — `RETURN sort-file` 行が return 対象の sort / merge file への参照を出すようになりました。
