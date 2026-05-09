---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ `decltype(Type{})` operands are indexed as type references** — unevaluated constructions such as `decltype(Widget{})` now expose `Widget` without adding a false instantiation edge.

## 日本語

- **C++ の `decltype(Type{})` operand を type reference として index するようになりました** — `decltype(Widget{})` のような未評価構築が、偽の instantiate edge を増やさずに `Widget` を出します。
