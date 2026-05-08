---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `refine` declarations now link to refined classes** — `cdidx` records targets from `refine String do` while suppressing the refinement keyword as call noise.

## 日本語

- **Ruby の `refine` 宣言が refinement 対象クラスへリンクするようになりました** — `cdidx` は `refine String do` の対象を索引し、キーワード自体の call ノイズは抑えます。
