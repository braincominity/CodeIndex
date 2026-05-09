---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# module abbreviations now expose their target namespace** — `module Json = System.Text.Json` keeps `Json` searchable while also indexing `System.Text.Json` as an import.

## 日本語

- **F# の module abbreviation が右辺の namespace も公開するようになりました** — `module Json = System.Text.Json` は `Json` を検索可能に保ちながら、`System.Text.Json` も import として索引します。
