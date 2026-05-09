---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `RaiseEvent [Name]` extraction so escaped event targets are indexed as calls under their unescaped names.

## 日本語
- Visual Basic の `RaiseEvent [Name]` 抽出で、エスケープされたイベント対象を非エスケープ名の call 参照として索引するよう修正しました。
