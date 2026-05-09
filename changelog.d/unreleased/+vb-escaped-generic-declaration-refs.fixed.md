---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic generic declaration detection so escaped owner names like `Class [Box](Of T)` do not index type parameters as references.

## 日本語
- Visual Basic のジェネリック宣言判定で、`Class [Box](Of T)` のようなエスケープされた宣言名でも型パラメーターを参照として索引しないよう修正しました。
