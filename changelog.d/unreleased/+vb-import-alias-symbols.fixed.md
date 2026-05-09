---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---
## English
- Fixed Visual Basic import alias indexing so `Imports Alias = Target.Type` is searchable by the alias name, including escaped aliases.

## 日本語
- Visual Basic の import alias 索引で、`Imports Alias = Target.Type` を alias 名で検索できるよう修正しました。エスケープされた alias も対象です。
