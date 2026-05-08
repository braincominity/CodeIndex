---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# backticked member names are now searchable** — members such as `member this.``display name`` = ...` are normalized and indexed instead of being skipped by the member pattern.

## 日本語

- **F# の backtick 付き member 名が検索できるようになりました** — `member this.``display name`` = ...` のようなmemberを正規化して索引します。
