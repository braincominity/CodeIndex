---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB `Declare` members are now searchable** — external `Declare Sub` and `Declare Function` declarations, including `Auto`, `Ansi`, and `Unicode` forms, are indexed as function symbols.

## 日本語

- **VB の `Declare` メンバーを検索できるようにしました** — `Auto`、`Ansi`、`Unicode` を含む外部 `Declare Sub` / `Declare Function` 宣言を function シンボルとして索引します。
