---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ concept constraints are indexed as type references** — `requires Serializable<T>` and `concept Persistable = ns::EntityLike<T>` now expose the referenced concept names in reference queries.

## 日本語

- **C++ の concept 制約を type reference として index するようになりました** — `requires Serializable<T>` や `concept Persistable = ns::EntityLike<T>` が参照先の concept 名を reference query に出します。
