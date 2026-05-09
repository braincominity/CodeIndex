---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **C++ constexpr constants are indexed** — Top-level `inline constexpr int kMaxConnections = ...` and uppercase `constexpr` constants now appear in symbol and definition searches.

## 日本語

- **C++ の constexpr 定数を index するようになりました** — top-level の `inline constexpr int kMaxConnections = ...` や大文字名の `constexpr` 定数が symbol / definition search に出るようになります。
