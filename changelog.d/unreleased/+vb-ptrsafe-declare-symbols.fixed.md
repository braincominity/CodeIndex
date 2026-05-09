---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic declaration indexing so `Declare PtrSafe Function` API declarations are searchable as functions.

## 日本語
- Visual Basic の宣言索引で、`Declare PtrSafe Function` の API 宣言を function として検索できるよう修正しました。
