---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# backticked open targets are now searchable** — `open ``Domain Helpers``` is indexed by the normalized module name.

## 日本語

- **F# の backtick 付き open target を検索できるようになりました** — `open ``Domain Helpers``` を正規化した module 名で索引します。
