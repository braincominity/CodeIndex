---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `autoload` constants are now indexed as references** — `cdidx` records the constant argument from `autoload :Name, "path"` so lazy-loaded Ruby types are easier to find.

## 日本語

- **Ruby の `autoload` 定数を参照として索引するようになりました** — `cdidx` は `autoload :Name, "path"` の定数引数を記録するため、遅延読み込みされる Ruby 型を見つけやすくなります。
