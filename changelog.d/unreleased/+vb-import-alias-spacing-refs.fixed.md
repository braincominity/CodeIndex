---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic import alias reference extraction so `Imports Alias=Target.Type` indexes the target type without treating the alias as a type reference.

## 日本語
- Visual Basic の import alias 参照抽出で、`Imports Alias=Target.Type` の右辺型だけを索引し、alias 左辺を型参照扱いしないよう修正しました。
