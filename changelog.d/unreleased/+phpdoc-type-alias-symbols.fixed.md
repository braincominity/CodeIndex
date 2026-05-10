---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc type aliases now emit type symbols** — `@phpstan-type`, `@psalm-type`, and plain `@type` aliases are now searchable with their aliased expression.

## 日本語

- **PHPDoc type alias を type シンボルとして索引するようになりました** — `@phpstan-type` / `@psalm-type` / 通常の `@type` alias が、alias 先の式とともに検索可能になります。
