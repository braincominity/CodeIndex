---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic member declaration indexing so escaped names like `Sub [Select]()` and `Property [Property]` are searchable under their unescaped names.

## 日本語
- Visual Basic のメンバー宣言索引で、`Sub [Select]()` や `Property [Property]` のようなエスケープ名を非エスケープ名で検索できるよう修正しました。
