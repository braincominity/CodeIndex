---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ C-style casts are indexed as type references** — casts such as `(Widget*)raw` and `(ns::Handle&)value` now expose their target types while primitive casts remain filtered.

## 日本語

- **C++ の C-style cast を type reference として index するようになりました** — `(Widget*)raw` や `(ns::Handle&)value` が変換先の型を出し、primitive cast は除外したままにします。
