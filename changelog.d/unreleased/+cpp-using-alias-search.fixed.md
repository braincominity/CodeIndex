---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **C++ `using` type aliases now show up in `symbols` search** — `using Alias = ...;` and `template <typename T> using Ptr = ...;` are now indexed as `import` symbols, which makes them discoverable through `symbols --lang cpp --kind import` and related lookups.

## 日本語

- **C++ の `using` 型エイリアスが `symbols` で見えるようになりました** — `using Alias = ...;` と `template <typename T> using Ptr = ...;` を `import` シンボルとして索引するため、`symbols --lang cpp --kind import` などの検索で直接見つけられます。
