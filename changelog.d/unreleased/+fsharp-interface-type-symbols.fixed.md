---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# inline interface declarations are now indexed as interfaces** — `type IFoo = interface ...` declarations are no longer surfaced as type aliases.

## 日本語

- **F# の inline interface declaration が interface として索引されるようになりました** — `type IFoo = interface ...` 形式の declaration を typealias として出さないようにしました。
