---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RubyReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Ruby DSL option hashes no longer add noisy references** — `cdidx` stops command-target scanning at Ruby keyword options such as `dependent:` or `only:`, keeping Rails-style reference results focused on real targets.

## 日本語

- **Ruby DSL の option hash がノイズ参照を追加しないようになりました** — `cdidx` は `dependent:` や `only:` のような Ruby keyword option で command target の走査を止め、Rails 風 DSL の参照結果を実際の対象に絞ります。
