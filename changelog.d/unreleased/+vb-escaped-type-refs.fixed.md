---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.PatternTypeReferences.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic type-reference extraction so escaped type names like `As [Class]` are indexed under their unescaped names.

## 日本語
- Visual Basic の型参照抽出で、`As [Class]` のようなエスケープ型名を非エスケープ名で索引するよう修正しました。
