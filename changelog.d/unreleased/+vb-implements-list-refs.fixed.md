---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `Implements` lists now index every interface type** — comma-separated declarations such as `Implements IRequestHandler, IAuditable` now emit `type_reference` rows for each implemented interface.

## 日本語

- **VB の `Implements` list で全 interface 型を索引するようにしました** — `Implements IRequestHandler, IAuditable` のような comma 区切り宣言が interface ごとに `type_reference` を出すようになりました。
