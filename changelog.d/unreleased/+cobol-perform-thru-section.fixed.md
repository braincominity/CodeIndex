---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - README.md
---

## English

- **COBOL `PERFORM ... THRU ...` now expands across sections and paragraphs** — COBOL paragraph and section headers are now both indexed, and `PERFORM ... THRU ...` emits call edges for the full inclusive range instead of only the direct start target.

## 日本語

- **COBOL の `PERFORM ... THRU ...` が section と paragraph をまたいで展開されるようになりました** — COBOL の paragraph / section ヘッダがどちらも索引されるようになり、`PERFORM ... THRU ...` は直接の start target だけでなく、含まれる範囲全体に call edge を出すようになりました。
