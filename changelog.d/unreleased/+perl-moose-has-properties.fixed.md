---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Perl Moose/Moo attributes are now searchable** - `has name => ...` and `has '+name' => ...` declarations are indexed as property symbols.

## 日本語

- **Perl Moose/Moo の属性を検索できるようになりました** - `has name => ...` と `has '+name' => ...` 宣言を property symbol としてインデックスします。
