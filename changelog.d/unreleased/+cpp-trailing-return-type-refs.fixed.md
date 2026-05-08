---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ trailing return types are indexed as type references** — `auto Make() -> Result` now exposes `Result` in reference queries while builtin return types stay filtered.

## 日本語

- **C++ の trailing return type を type reference として index するようになりました** — `auto Make() -> Result` が `Result` を reference query に出し、組み込み戻り値型は除外したままにします。
