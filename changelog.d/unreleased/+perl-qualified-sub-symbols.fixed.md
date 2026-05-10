---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Perl qualified subroutine definitions are now searchable** - `sub My::Module::name` declarations are indexed as function symbols using their full qualified names.

## 日本語

- **Perl の qualified subroutine 定義を検索できるようになりました** - `sub My::Module::name` 宣言を完全修飾名の function symbol としてインデックスします。
