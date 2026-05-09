---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ pointer-to-member types are indexed as type references** — declarations such as `int Widget::* field` and `void (ns::Handler::*callback)()` now expose the receiver type in reference queries.

## 日本語

- **C++ の pointer-to-member 型を type reference として index するようになりました** — `int Widget::* field` や `void (ns::Handler::*callback)()` が receiver 型を reference query に出します。
