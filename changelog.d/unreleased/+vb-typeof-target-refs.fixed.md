---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `TypeOf ... Is` target types are now indexed** — type-test expressions now emit `type_reference` rows, including `IsNot` and qualified type names.

## 日本語

- **VB の `TypeOf ... Is` 対象型を索引するようにしました** — `IsNot` や修飾名を含む型テスト式が `type_reference` を出すようになりました。
