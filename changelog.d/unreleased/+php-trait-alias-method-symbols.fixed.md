---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP trait adaptation aliases now emit function symbols** — methods introduced with `Trait::method as aliasName;` are now searchable, while visibility-only adaptations are ignored.

## 日本語

- **PHP trait adaptation の alias を function シンボルとして索引するようになりました** — `Trait::method as aliasName;` で導入されるメソッドを検索可能にし、visibility 変更だけの adaptation は除外します。
