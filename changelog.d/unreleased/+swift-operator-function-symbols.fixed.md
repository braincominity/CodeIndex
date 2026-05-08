---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Swift operator overload functions are now indexed as symbols** — declarations such as `static func +`, `static func ==`, and `prefix func !` now appear in symbol search instead of being skipped by identifier-only function matching.

## 日本語

- **Swift の operator overload 関数をシンボルとして index するようにしました** — `static func +` / `static func ==` / `prefix func !` のような宣言を、識別子限定の関数マッチで取りこぼさず symbol search に出します。
