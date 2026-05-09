---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic call reference extraction so escaped calls like `[Select]()` and `Me.[Save]()` are indexed under their normal names.

## 日本語
- Visual Basic の呼び出し参照抽出で、`[Select]()` や `Me.[Save]()` のようなエスケープ呼び出しを通常名で索引できるよう修正しました。
