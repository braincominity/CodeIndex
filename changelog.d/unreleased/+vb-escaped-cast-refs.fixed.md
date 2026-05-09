---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic cast target extraction so escaped target types like `DirectCast(raw, [Class])` are indexed under their unescaped names.

## 日本語
- Visual Basic のキャスト対象抽出で、`DirectCast(raw, [Class])` のようなエスケープ対象型を非エスケープ名で索引するよう修正しました。
