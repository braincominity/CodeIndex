---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby receiver-qualified singleton methods are now indexed by method name** — `cdidx` records `export!` from `def Admin::User.export!` instead of mistaking the receiver for the function symbol.

## 日本語

- **Ruby の receiver 付き singleton method をメソッド名で索引するようになりました** — `cdidx` は `def Admin::User.export!` から receiver ではなく `export!` を関数シンボルとして記録します。
