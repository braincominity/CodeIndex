---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# constrained generic classes are now indexed as classes** — declarations such as `type Box<'T when 'T : not struct> = class end` remain searchable by class name.

## 日本語

- **F# の制約付き generic class を class として索引するようになりました** — `type Box<'T when 'T : not struct> = class end` のような宣言を class 名で検索できます。
