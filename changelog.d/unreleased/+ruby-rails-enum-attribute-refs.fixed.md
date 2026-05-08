---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby Rails `enum` declarations now index attribute names** — `cdidx` records the attribute passed to `enum :status` while leaving enum values and options out of reference search.

## 日本語

- **Ruby Rails の `enum` 宣言が属性名を索引するようになりました** — `cdidx` は `enum :status` の属性名を記録し、enum値やオプションは参照検索に混ぜません。
