---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic type declaration indexing so escaped identifiers like `Class [Class]` are searchable under their unescaped names.

## 日本語
- Visual Basic の型宣言索引で、`Class [Class]` のようなエスケープ識別子を非エスケープ名で検索できるよう修正しました。
