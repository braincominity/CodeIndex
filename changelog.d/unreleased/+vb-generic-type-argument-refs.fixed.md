---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB generic type arguments are indexed without generic-parameter noise** — `Repository(Of Customer)` still emits `Customer`, while declarations like `Class Box(Of T)` no longer emit phantom `T` type references.

## 日本語

- **VB の generic 型引数を、型パラメータのノイズなしで索引するようにしました** — `Repository(Of Customer)` は引き続き `Customer` を出し、`Class Box(Of T)` のような宣言では phantom な `T` type reference を出しません。
