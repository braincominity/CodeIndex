---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails `attribute` declarations now index attribute names** — `cdidx` records the first argument from `attribute :timezone` while ignoring type and option values.

## 日本語

- **Ruby Rails の `attribute` 宣言が属性名を索引するようになりました** — `cdidx` は `attribute :timezone` の第一引数を記録し、型やoption値は参照に混ぜません。
