---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# backticked module names are now searchable** — module declarations whose names are escaped with F# backticks are normalized and indexed as namespace symbols.

## 日本語

- **F# の backtick 付き module 名が検索できるようになりました** — F# backtick でエスケープされた module 宣言名を正規化し、namespace symbol として索引します。
