---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Perl package variables are now searchable** - `our $VERSION`, `our @EXPORT_OK`, and similar package variables are indexed as property symbols.

## 日本語

- **Perl の package 変数を検索できるようになりました** - `our $VERSION`、`our @EXPORT_OK` などの package 変数を property symbol としてインデックスします。
