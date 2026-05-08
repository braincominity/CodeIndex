---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **F# auto-properties are now searchable** — `member val DisplayName = ...` is indexed as a `property` symbol instead of being skipped by the regular member extractor.

## 日本語

- **F# auto-property が検索できるようになりました** — `member val DisplayName = ...` を通常member抽出で除外したまま、`property` シンボルとして索引します。
