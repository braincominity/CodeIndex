---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ using-alias targets are indexed as type references** — `using Ptr = std::unique_ptr<Service>;` now exposes the aliased target types in reference queries.

## 日本語

- **C++ using alias の右辺型を type reference として index するようになりました** — `using Ptr = std::unique_ptr<Service>;` が alias 先の型を reference query に出します。
