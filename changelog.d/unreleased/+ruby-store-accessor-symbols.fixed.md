---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby Rails `store_accessor` declarations now create property symbols** — `cdidx` indexes generated accessors such as `theme` and `locale` from `store_accessor :settings, :theme, :locale`.

## 日本語

- **Ruby Rails の `store_accessor` 宣言がproperty symbolを作るようになりました** — `cdidx` は `store_accessor :settings, :theme, :locale` から生成される `theme` や `locale` accessorを索引します。
