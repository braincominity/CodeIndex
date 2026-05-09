---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ typedef alias targets are indexed as type references** — `typedef ns::Handler* HandlerPtr;` now exposes the aliased target type in reference queries while still avoiding function-pointer typedefs.

## 日本語

- **C++ typedef alias の元型を type reference として index するようになりました** — `typedef ns::Handler* HandlerPtr;` が alias 元の型を reference query に出し、関数ポインタ typedef は引き続き避けます。
