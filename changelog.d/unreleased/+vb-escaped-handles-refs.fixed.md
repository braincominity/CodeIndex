---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `Handles` extraction so escaped event targets like `Handles button.[Click]` are indexed under their unescaped event names.

## 日本語
- Visual Basic の `Handles` 抽出で、`Handles button.[Click]` のようなエスケープされたイベント対象を非エスケープ名で索引するよう修正しました。
