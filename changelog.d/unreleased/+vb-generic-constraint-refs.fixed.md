---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---
## English
- Fixed Visual Basic generic declarations so constraint types such as `Of T As IDisposable` are indexed without treating the type parameter as a reference.

## 日本語
- Visual Basic のジェネリック宣言で、`Of T As IDisposable` のような制約型を索引しつつ、型パラメーター自体は参照扱いしないよう修正しました。
