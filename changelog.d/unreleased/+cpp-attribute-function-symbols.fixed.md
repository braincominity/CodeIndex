---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ attribute-prefixed functions are searchable** — declarations such as `[[nodiscard]] int compute() {}` now index the function name instead of being skipped at the attribute prefix.

## 日本語

- **C++ attribute 前置き付き関数を検索できるようになりました** — `[[nodiscard]] int compute() {}` のような宣言で、attribute 前置きに阻まれず関数名を index します。
