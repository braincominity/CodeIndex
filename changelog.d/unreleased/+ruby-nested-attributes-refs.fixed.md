---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails nested attribute declarations now index association names** — `cdidx` records associations passed to `accepts_nested_attributes_for` while stopping before option keys.

## 日本語

- **Ruby Rails のnested attribute宣言がassociation名を索引するようになりました** — `cdidx` は `accepts_nested_attributes_for` に渡されたassociationを記録し、option keyの手前で停止します。
