---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# `use` bindings are now searchable** — resource bindings such as `use client = ...` and `use! lease = ...` are indexed as lightweight symbols.

## 日本語

- **F# の `use` binding が検索できるようになりました** — `use client = ...` や `use! lease = ...` のようなresource bindingを軽量symbolとして索引します。
