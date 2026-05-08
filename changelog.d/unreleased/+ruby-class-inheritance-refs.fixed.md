---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby superclass references are now indexed** — `cdidx` records the parent class in `class Child < Parent`, allowing searches for Ruby types to find inheritance usage.

## 日本語

- **Ruby の親クラス参照を索引するようになりました** — `cdidx` は `class Child < Parent` の親クラスを記録するため、Ruby 型の検索で継承利用箇所を見つけられます。
