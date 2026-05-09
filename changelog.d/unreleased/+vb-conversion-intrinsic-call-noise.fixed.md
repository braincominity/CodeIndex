---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic reference extraction so built-in conversion intrinsics such as `CInt` and `CStr` are not indexed as call references.

## 日本語
- Visual Basic の参照抽出で、`CInt` や `CStr` などの組み込み変換関数を call 参照として索引しないよう修正しました。
