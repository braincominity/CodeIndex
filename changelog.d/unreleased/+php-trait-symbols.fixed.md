---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP trait declarations now get their own symbol kind** — `trait Foo {}` is indexed as `trait` instead of `interface`, so PHP projects surface trait names more accurately in symbol search and navigation.

## 日本語

- **PHP の trait 宣言が専用のシンボル種別で索引されるようになりました** — `trait Foo {}` は `interface` ではなく `trait` として索引されるため、PHP プロジェクトで trait 名がシンボル検索とナビゲーションにより正確に現れます。
