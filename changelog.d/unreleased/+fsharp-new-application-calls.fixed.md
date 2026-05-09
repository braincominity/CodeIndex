---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# constructor applications without parentheses are now indexed** — `new Customer user` records `Customer` as an instantiation target.

## 日本語

- **F# の括弧なし constructor application を索引するようになりました** — `new Customer user` で `Customer` を instantiation target として記録します。
