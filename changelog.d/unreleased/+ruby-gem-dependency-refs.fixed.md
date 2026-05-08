---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `gem` declarations now index dependency names** — `cdidx` records the first string argument from `gem "name"` so Gemfile dependency searches can find package declarations without keyword noise.

## 日本語

- **Ruby の `gem` 宣言が依存名を索引するようになりました** — `cdidx` は `gem "name"` の最初の文字列引数を記録するため、Gemfile の依存宣言をキーワードノイズなしで検索できます。
