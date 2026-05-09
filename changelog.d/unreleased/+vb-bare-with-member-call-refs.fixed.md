---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic call reference extraction so bare `With` member calls such as `.Refresh user` are indexed.

## 日本語
- Visual Basic の呼び出し参照抽出で、`.Refresh user` のような `With` 内の括弧なし member 呼び出しを索引するよう修正しました。
