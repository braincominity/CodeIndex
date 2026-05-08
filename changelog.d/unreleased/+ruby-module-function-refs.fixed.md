---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby `module_function` declarations now link to exported methods** — `cdidx` indexes method names passed to `module_function` while suppressing the keyword as call noise.

## 日本語

- **Ruby の `module_function` 宣言が公開メソッドへリンクするようになりました** — `cdidx` は `module_function` に渡されたメソッド名を索引し、キーワード自体の call ノイズは抑えます。
