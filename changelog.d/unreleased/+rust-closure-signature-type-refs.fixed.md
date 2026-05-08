---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust closure signatures now emit type references** — typed closure parameters and explicit closure return types are indexed so closure-local dependencies participate in search and impact analysis.

## 日本語

- **Rust closure signature が type reference を出すようになりました** — 型付き closure 引数と明示的な戻り値型を索引し、closure 内の依存も検索と影響分析に反映します。
