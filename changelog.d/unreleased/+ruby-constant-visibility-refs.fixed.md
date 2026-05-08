---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby constant visibility declarations now link to constants** — `cdidx` indexes targets from `private_constant` and `public_constant` while suppressing keyword call noise.

## 日本語

- **Ruby の定数 visibility 宣言が定数へリンクするようになりました** — `cdidx` は `private_constant` / `public_constant` の対象を索引し、キーワード自体の call ノイズは抑えます。
