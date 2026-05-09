---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic generic constraint extraction so escaped type parameters in declarations like `Of [T] As [Class]` do not leak as references while the constraint type is indexed.

## 日本語
- Visual Basic のジェネリック制約抽出で、`Of [T] As [Class]` のような宣言のエスケープ型パラメーターを参照として漏らさず、制約型だけを索引するよう修正しました。
