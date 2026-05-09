---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ friend declarations are indexed as type references** — `friend class Inspector;`, `friend struct ns::Peer;`, and `friend enum class Status;` now expose the referenced types in reference queries.

## 日本語

- **C++ の friend 宣言を type reference として index するようになりました** — `friend class Inspector;`、`friend struct ns::Peer;`、`friend enum class Status;` が参照先の型を reference query に出します。
