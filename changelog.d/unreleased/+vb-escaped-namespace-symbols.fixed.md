---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic namespace indexing so escaped segments like `Namespace [My].App` are searchable as `My.App`.

## 日本語
- Visual Basic の名前空間索引で、`Namespace [My].App` のようなエスケープセグメントを `My.App` として検索できるよう修正しました。
