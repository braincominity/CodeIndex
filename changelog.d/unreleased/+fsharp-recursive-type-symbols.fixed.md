---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# recursive type declarations are now searchable by type name** — `type rec Tree<'T> = ...` is indexed as a type symbol instead of being skipped by the top-level F# patterns.

## 日本語

- **F# の recursive type declaration が型名で検索できるようになりました** — `type rec Tree<'T> = ...` が上位の F# pattern でスキップされず、型シンボルとして索引されます。
