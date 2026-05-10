---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Php.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **PHP constructor-promoted properties are now searchable** — same-line `__construct(public string $id, ...)` declarations now emit `property` symbols for promoted members without indexing ordinary parameters.

## 日本語

- **PHP の constructor promotion プロパティを検索できるようになりました** — 同一行の `__construct(public string $id, ...)` 宣言から promotion されたメンバーを `property` シンボルとして出し、通常パラメータは索引しません。
