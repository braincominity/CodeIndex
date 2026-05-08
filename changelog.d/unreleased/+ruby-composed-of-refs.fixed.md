---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails `composed_of` declarations now link aggregate and class targets** — `cdidx` records the aggregate name and `class_name` target from `composed_of :address`.

## 日本語

- **Ruby Rails の `composed_of` 宣言がaggregate名とclass targetへリンクするようになりました** — `cdidx` は `composed_of :address` のaggregate名と `class_name` targetを記録します。
