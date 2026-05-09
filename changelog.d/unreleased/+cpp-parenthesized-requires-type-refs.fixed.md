---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Parenthesized C++ requires concepts are indexed as type references** — constraints such as `requires (Serializable<T>)` and `requires(ns::EntityLike<T>)` now expose the concept names in reference queries.

## 日本語

- **括弧付き C++ requires concept を type reference として index するようになりました** — `requires (Serializable<T>)` や `requires(ns::EntityLike<T>)` が concept 名を reference query に出します。
