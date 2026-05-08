---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust path-qualified `impl` blocks now attach to the implementing type** — `impl crate::models::Widget {}` and trait impls for qualified targets now surface `Widget` instead of a path prefix as the searchable impl symbol.

## 日本語

- **Rust の path-qualified な `impl` block を実装対象の型へ紐づけるようになりました** — `impl crate::models::Widget {}` や修飾付き target の trait impl で、検索対象の impl シンボルが path prefix ではなく `Widget` になります。
