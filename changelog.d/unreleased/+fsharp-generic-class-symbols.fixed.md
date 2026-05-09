---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# generic class declarations are now searchable** — declarations such as `type Box<'T> = class end` and generic constructor forms are indexed as class symbols.

## 日本語

- **F# の generic class 宣言が検索できるようになりました** — `type Box<'T> = class end` や generic constructor 形式を class symbol として索引します。
