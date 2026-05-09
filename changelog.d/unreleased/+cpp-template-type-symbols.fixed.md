---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ template classes and structs declared on one line are searchable** — `template <typename T> class Box {}` and matching struct declarations now index their type symbols.

## 日本語

- **1 行の C++ template class / struct 宣言を検索できるようになりました** — `template <typename T> class Box {}` などの型 symbol を index します。
