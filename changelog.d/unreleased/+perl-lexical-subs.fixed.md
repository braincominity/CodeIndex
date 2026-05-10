---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Perl lexical subroutines are now searchable** - `my sub name` and `state sub name` declarations are indexed as function symbols alongside ordinary `sub name` declarations.

## 日本語

- **Perl の lexical subroutine を検索できるようになりました** - `my sub name` と `state sub name` 宣言を通常の `sub name` と同じく function symbol としてインデックスします。
