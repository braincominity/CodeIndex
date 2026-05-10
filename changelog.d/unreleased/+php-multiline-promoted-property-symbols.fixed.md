---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP multiline constructor promotion is now searchable** — promoted properties declared on separate `__construct(` parameter lines now emit `property` symbols while ordinary parameters remain ignored.

## 日本語

- **PHP の複数行 constructor promotion を検索できるようになりました** — `__construct(` の各パラメータ行に書かれた promoted property を `property` シンボルとして出し、通常パラメータは引き続き無視します。
