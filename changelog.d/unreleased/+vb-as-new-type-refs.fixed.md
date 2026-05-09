---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `As New` reference extraction so the constructed type is indexed instead of `New`.

## 日本語
- Visual Basic の `As New` 参照抽出で、`New` ではなく生成対象の型を索引するよう修正しました。
