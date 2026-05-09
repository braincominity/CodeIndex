---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB object creation now emits instantiate references** — `New Customer()` and qualified constructor targets now appear as `instantiate` edges while anonymous `New With` objects stay ignored.

## 日本語

- **VB のオブジェクト生成が instantiate reference を出すようになりました** — `New Customer()` や修飾 constructor 対象が `instantiate` edge になり、匿名 `New With` オブジェクトは除外します。
