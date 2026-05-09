---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ dynamic exception specifications are indexed as type references** — legacy signatures such as `void load() throw(Error, ns::Failure&)` now expose the listed exception types without treating parenthesized throw expressions as types.

## 日本語

- **C++ の dynamic exception specification を type reference として index するようになりました** — `void load() throw(Error, ns::Failure&)` のような旧式signatureが列挙された例外型を出し、括弧付きthrow式は型扱いしません。
