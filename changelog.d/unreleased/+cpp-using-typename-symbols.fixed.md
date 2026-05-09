---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Cpp.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ `using typename` declarations are indexed as imports** — dependent declarations such as `using typename Base::value_type;` now appear in symbol and definition searches with their enclosing scope.

## 日本語

- **C++ の `using typename` 宣言を import として index するようになりました** — `using typename Base::value_type;` のような依存宣言が、囲むscope付きで symbol / definition search に出ます。
