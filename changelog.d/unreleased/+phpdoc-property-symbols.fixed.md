---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc dynamic properties now emit property symbols** — `@property`, `@property-read`, and `@property-write` declarations are now searchable with their documented types.

## 日本語

- **PHPDoc の動的プロパティを property シンボルとして索引するようになりました** — `@property` / `@property-read` / `@property-write` 宣言が、記録された型とともに検索可能になります。
