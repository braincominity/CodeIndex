---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `RemoveHandler` extraction so escaped event targets like `RemoveHandler button.[Click], ...` are indexed under their unescaped event names.

## 日本語
- Visual Basic の `RemoveHandler` 抽出で、`RemoveHandler button.[Click], ...` のようなエスケープされたイベント対象を非エスケープ名で索引するよう修正しました。
