---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB iterator members are now searchable as functions** — `Iterator Function` and `Iterator Sub` declarations are indexed as function symbols instead of being skipped when searching Visual Basic code.

## 日本語

- **VB の iterator メンバーを function として検索できるようにしました** — `Iterator Function` と `Iterator Sub` 宣言を Visual Basic コード検索で読み飛ばさず、function シンボルとして索引します。
