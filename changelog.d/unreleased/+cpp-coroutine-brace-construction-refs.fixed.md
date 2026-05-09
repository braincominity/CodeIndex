---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ coroutine braced returns are indexed as instantiations** — `co_return Result{}` now exposes the constructed coroutine result type in reference and instantiation queries.

## 日本語

- **C++ coroutine の braced return を instantiate として index するようになりました** — `co_return Result{}` が構築する coroutine 戻り値型を reference / instantiation query に出します。
