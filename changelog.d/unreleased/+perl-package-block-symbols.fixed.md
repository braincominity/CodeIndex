---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Perl package block declarations are now searchable** - `package My::Module { ... }` declarations are indexed as namespace symbols, matching the existing `package My::Module;` support.

## 日本語

- **Perl の package block 宣言を検索できるようになりました** - `package My::Module { ... }` 宣言を namespace symbol としてインデックスし、既存の `package My::Module;` 対応と揃えました。
