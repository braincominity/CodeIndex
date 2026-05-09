---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `AddHandler` extraction so escaped event targets like `AddHandler button.[Click], ...` are indexed under their unescaped event names.

## 日本語
- Visual Basic の `AddHandler` 抽出で、`AddHandler button.[Click], ...` のようなエスケープされたイベント対象を非エスケープ名で索引するよう修正しました。
