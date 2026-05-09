---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic `RemoveHandler` reference extraction so deeply qualified event targets index the final event name.

## 日本語
- Visual Basic の `RemoveHandler` 参照抽出で、多段修飾されたイベント対象の末尾イベント名を索引するよう修正しました。
