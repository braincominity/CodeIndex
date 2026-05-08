---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `using` refinement targets are now indexed as references** — `cdidx` records the refinement module in `using SomeRefinement` without adding a noisy call edge for the keyword.

## 日本語

- **Ruby の `using` refinement 対象を参照として索引するようになりました** — `cdidx` は `using SomeRefinement` の refinement モジュールを記録し、キーワード自体のノイズになる call edge は追加しません。
