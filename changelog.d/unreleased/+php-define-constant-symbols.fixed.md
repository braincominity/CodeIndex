---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP `define()` constants are now searchable** — literal global constants declared with `define('NAME', ...)` or `define("NAME", ...)` now appear as symbols.

## 日本語

- **PHP の `define()` 定数を検索できるようになりました** — `define('NAME', ...)` や `define("NAME", ...)` で宣言された literal なグローバル定数をシンボルとして出します。
