---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# delegate type declarations are now indexed as delegates** — `type Handler = delegate of ...` is searchable by the declared delegate name instead of being treated as an alias/import fallback.

## 日本語

- **F# の delegate type declaration が delegate として索引されるようになりました** — `type Handler = delegate of ...` を alias/import の fallback ではなく、宣言された delegate 名で検索できます。
