---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **C++ `typedef` aliases are now indexed alongside `using` aliases** — `symbols --lang cpp --kind import` now picks up simple `typedef ExistingType Alias;` declarations in addition to `using Alias = ...;`, and namespace-local alias declarations are covered by regression tests.

## 日本語

- **C++ の `typedef` エイリアスが `using` エイリアスと同様に索引されるようになりました** — `symbols --lang cpp --kind import` は `using Alias = ...;` に加えて `typedef ExistingType Alias;` のような単純な宣言も拾うようになり、名前空間内のローカルなエイリアス宣言も回帰テストでカバーしました。
