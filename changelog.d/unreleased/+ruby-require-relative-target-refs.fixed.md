---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `require_relative` targets are now indexed as references** — `cdidx` now records the relative path argument from `require_relative "..."`, making Ruby local dependency searches more complete.

## 日本語

- **Ruby の `require_relative` 対象を参照として索引するようになりました** — `cdidx` は `require_relative "..."` の相対パス引数を記録するため、Ruby のローカル依存検索がより完全になります。
