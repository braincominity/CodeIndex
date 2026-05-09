---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `GetType` target types are now indexed** — `GetType(Customer)` and qualified forms now emit `type_reference` rows so reflection-only Visual Basic dependencies are searchable.

## 日本語

- **VB の `GetType` 対象型を索引するようにしました** — `GetType(Customer)` や修飾名の型が `type_reference` になり、reflection でしか現れない Visual Basic の依存関係も検索できます。
