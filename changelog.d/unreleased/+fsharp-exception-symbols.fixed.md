---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# `exception` declarations are now indexed as searchable symbols** — `SymbolExtractor` now records top-level F# `exception` definitions, including backtick-escaped names, so exception types appear in `symbols`, `search`, and `impact` results.

## 日本語

- **F# の `exception` 宣言が検索可能な symbol として索引されるようになりました** — `SymbolExtractor` がトップレベルの F# `exception` 定義を記録するため、バッククォートで囲まれた名前も含めて例外型を `symbols` / `search` / `impact` から直接引けます。
