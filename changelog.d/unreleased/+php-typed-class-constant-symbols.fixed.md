---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP typed class constants are now indexed by constant name** — declarations such as `public const string VERSION = ...` now emit `VERSION` with the declared type instead of being missed.

## 日本語

- **PHP の型付きクラス定数を定数名で索引するようになりました** — `public const string VERSION = ...` のような宣言を取り落とさず、宣言型付きの `VERSION` として出します。
