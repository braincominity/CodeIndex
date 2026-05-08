---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails `serialize` declarations now index serialized attribute names** — `cdidx` records `serialize :settings` without indexing serializer option keys or values.

## 日本語

- **Ruby Rails の `serialize` 宣言がserialized属性名を索引するようになりました** — `cdidx` は `serialize :settings` を記録し、serializer option keyや値は参照に混ぜません。
