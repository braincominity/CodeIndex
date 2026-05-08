---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# `match!` input calls are now indexed** — computation expression inputs such as `match! fetch value with` record `fetch` as a call.

## 日本語

- **F# の `match!` 入力側呼び出しを索引するようになりました** — computation expression の `match! fetch value with` で `fetch` を call として記録します。
