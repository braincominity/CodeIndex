---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ `using enum` declarations are searchable** — `using enum ns::Color;` now indexes the introduced enum type as an import symbol.

## 日本語

- **C++ の `using enum` 宣言を検索できるようになりました** — `using enum ns::Color;` が導入対象の enum 型を import symbol として index します。
