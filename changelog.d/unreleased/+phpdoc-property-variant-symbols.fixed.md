---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc analyzer property tags now emit property symbols** — `@phpstan-property*` and `@psalm-property*` declarations are now indexed like regular `@property` tags.

## 日本語

- **PHPDoc analyzer property tag を property シンボルとして索引するようになりました** — `@phpstan-property*` / `@psalm-property*` 宣言を通常の `@property` と同様に扱います。
