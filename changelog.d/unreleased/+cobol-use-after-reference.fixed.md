---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/CobolReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **COBOL declarative `USE AFTER` file targets are now searchable** — `USE AFTER ... PROCEDURE ON file-name` lines now emit references to their handled file.

## 日本語

- **COBOL declarative の `USE AFTER` file target を検索可能にしました** — `USE AFTER ... PROCEDURE ON file-name` 行が処理対象 file への参照を出すようになりました。
