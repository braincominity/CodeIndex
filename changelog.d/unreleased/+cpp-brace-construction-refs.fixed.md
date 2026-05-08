---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ braced constructions are indexed as instantiations** — `auto x = Widget{}` and `return ns::Result{}` now expose the constructed type in reference queries.

## 日本語

- **C++ の braced construction を instantiation として index するようになりました** — `auto x = Widget{}` や `return ns::Result{}` が、構築される型を reference query に出します。
