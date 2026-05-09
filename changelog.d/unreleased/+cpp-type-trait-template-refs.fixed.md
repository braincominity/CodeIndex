---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ type trait template arguments are indexed as type references** — expressions such as `std::is_same_v<T, Widget>` and `is_base_of<Base, Derived>` now expose the participating types in reference queries.

## 日本語

- **C++ の type trait template 引数を type reference として index するようになりました** — `std::is_same_v<T, Widget>` や `is_base_of<Base, Derived>` が関与する型を reference query に出します。
