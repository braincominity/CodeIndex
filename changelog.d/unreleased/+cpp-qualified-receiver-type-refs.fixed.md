---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ qualified member receivers are indexed as type references** — `Widget::Create()` and `&Widget::Run` now expose `Widget` in reference queries without treating lowercase namespaces like `std` as types.

## 日本語

- **C++ qualified member receiver を type reference として index するようになりました** — `Widget::Create()` や `&Widget::Run` が `Widget` を reference query に出し、`std` のような小文字 namespace は型扱いしません。
