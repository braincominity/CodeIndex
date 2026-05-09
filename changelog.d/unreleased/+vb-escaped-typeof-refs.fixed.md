---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `TypeOf ... Is [Type]` extraction so escaped target types are indexed under their unescaped names.

## 日本語
- Visual Basic の `TypeOf ... Is [Type]` 抽出で、エスケープ対象型を非エスケープ名で索引するよう修正しました。
