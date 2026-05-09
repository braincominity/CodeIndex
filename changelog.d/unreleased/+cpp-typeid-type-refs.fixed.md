---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **C++ `typeid(Type)` operands are indexed** — RTTI checks such as `typeid(Service)` now add type-reference rows for the named type.

## 日本語

- **C++ の `typeid(Type)` operand を index するようになりました** — `typeid(Service)` のような RTTI チェックが、対象型の type-reference 行を追加します。
