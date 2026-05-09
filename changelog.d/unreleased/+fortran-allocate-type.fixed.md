---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Fortran typed `allocate` statements now record allocation type references** — `allocate(TypeName :: value)` now indexes `TypeName` as a type reference so dynamic allocation dependencies are searchable.

## 日本語

- **Fortran の型付き `allocate` 文が確保型の参照を記録するようになりました** — `allocate(TypeName :: value)` の `TypeName` を型参照として索引し、動的確保の依存を検索できるようにしました。
