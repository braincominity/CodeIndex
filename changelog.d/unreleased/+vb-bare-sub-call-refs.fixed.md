---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic call reference extraction so bare Sub-call statements such as `Save` and `Me.Refresh user` are indexed.

## 日本語
- Visual Basic の呼び出し参照抽出で、`Save` や `Me.Refresh user` のような括弧なし Sub 呼び出し文を索引するよう修正しました。
