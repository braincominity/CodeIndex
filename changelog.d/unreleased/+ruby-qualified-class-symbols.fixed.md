---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby qualified class and module names are now indexed fully** — `cdidx` records names such as `Admin::Billing::Invoice` instead of only the first namespace segment.

## 日本語

- **Ruby の修飾付き class / module 名を完全な名前で索引するようになりました** — `cdidx` は `Admin::Billing::Invoice` のような名前を先頭 namespace セグメントだけでなく完全な名前として記録します。
