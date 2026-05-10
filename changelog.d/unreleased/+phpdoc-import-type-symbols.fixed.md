---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHPDoc imported type aliases now emit type symbols** — `@phpstan-import-type` and `@psalm-import-type` imports are now searchable, using the local `as` alias when present.

## 日本語

- **PHPDoc import-type alias を type シンボルとして索引するようになりました** — `@phpstan-import-type` / `@psalm-import-type` import が検索可能になり、`as` alias がある場合はローカル alias 名を使います。
