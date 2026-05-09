---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/RReferenceExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **R `sys.source()` file loads now match `source()` search coverage** — sourced paths are indexed as import symbols and emitted as references.

## 日本語

- **R の `sys.source()` ファイル読み込みが `source()` と同じ検索対象になりました** — source 先パスを import シンボルとして索引し、参照としても記録します。
