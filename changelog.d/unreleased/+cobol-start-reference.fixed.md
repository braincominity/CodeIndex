---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL `START` statements are now searchable** — `START file-name` lines now emit a reference to the indexed file target, improving navigation through COBOL indexed-file access.

## 日本語

- **COBOL の `START` 文を検索可能にしました** — `START file-name` 行が索引ファイル対象への参照を出すようになり、COBOL の indexed-file access をたどりやすくなりました。
