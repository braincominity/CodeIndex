---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP trait-use adaptation blocks now emit type references** — `use A, B { ... }` now records the trait names before the adaptation block.

## 日本語

- **PHP の trait-use adaptation block を型参照として索引するようになりました** — `use A, B { ... }` で adaptation block の前にある trait 名を記録します。
