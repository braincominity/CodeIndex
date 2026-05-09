---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ throw braced constructions are indexed as instantiations** — `throw Error{}` and `throw ns::Failure{}` now expose the thrown type in reference and instantiation queries.

## 日本語

- **C++ の throw braced construction を instantiate として index するようになりました** — `throw Error{}` や `throw ns::Failure{}` が throw される型を reference / instantiation query に出します。
