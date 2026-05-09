---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ `sizeof` and `alignof` type operands are indexed** — type operands such as `sizeof(Widget)` and `alignof(ns::Packet)` now produce `type_reference` rows.

## 日本語

- **C++ の `sizeof` / `alignof` 型 operand を index するようになりました** — `sizeof(Widget)` や `alignof(ns::Packet)` が `type_reference` 行を出します。
