---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Named Fortran interfaces are now searchable containers** — `interface math_iface` is indexed as a `namespace` symbol, and procedures declared inside it now attach to that interface instead of falling back to the surrounding module.

## 日本語

- **名前付き Fortran interface を検索可能な container として扱うようになりました** — `interface math_iface` を `namespace` シンボルとして索引し、その中で宣言された procedure が周囲の module ではなく interface にぶら下がるようになります。
