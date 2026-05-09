---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `CallByName` reference extraction so the string target is indexed as the call instead of `CallByName` itself.

## 日本語
- Visual Basic の `CallByName` 参照抽出で、`CallByName` 自体ではなく文字列で指定された対象を call として索引するよう修正しました。
