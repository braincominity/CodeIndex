---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Qualified C++ requires concepts are indexed as type references** — constraints such as `requires std::derived_from<T, Base>` now expose both the qualified concept and its type arguments.

## 日本語

- **修飾付き C++ requires concept を type reference として index するようになりました** — `requires std::derived_from<T, Base>` のような制約が、修飾付きconceptと型引数の両方を出します。
