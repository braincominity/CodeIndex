---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# constrained constructor-style classes are now indexed** — declarations such as `type Factory<'T when 'T : not struct>(value: 'T) = class end` remain searchable by class name.

## 日本語

- **F# の制約付き constructor-style class を索引するようになりました** — `type Factory<'T when 'T : not struct>(value: 'T) = class end` のような宣言を class 名で検索できます。
