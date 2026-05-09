---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `New [Type]()` extraction so escaped constructed types are indexed as `instantiate` references under their unescaped names.

## 日本語
- Visual Basic の `New [Type]()` 抽出で、エスケープされた生成型を非エスケープ名の `instantiate` 参照として索引するよう修正しました。
