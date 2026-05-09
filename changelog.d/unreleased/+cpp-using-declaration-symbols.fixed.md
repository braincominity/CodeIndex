---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Cpp.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ `using ns::Type;` declarations are indexed as imports** — direct using declarations now appear in symbol and definition searches with their enclosing scope.

## 日本語

- **C++ の `using ns::Type;` 宣言を import として index するようになりました** — 直接using宣言が、囲むscope付きで symbol / definition search に出ます。
