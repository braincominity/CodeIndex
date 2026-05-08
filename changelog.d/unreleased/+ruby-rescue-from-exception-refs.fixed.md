---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `rescue_from` declarations now index exception classes** — `cdidx` records exception targets from Rails-style `rescue_from` declarations while avoiding option-hash noise.

## 日本語

- **Ruby の `rescue_from` 宣言が例外クラスを索引するようになりました** — `cdidx` は Rails 風 `rescue_from` 宣言の例外対象を記録し、option hash 由来のノイズは避けます。
