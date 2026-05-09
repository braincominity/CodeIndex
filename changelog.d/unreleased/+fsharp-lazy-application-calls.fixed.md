---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# lazy expression calls are now indexed** — `lazy compute user` records `compute` as a call while suppressing the `lazy` keyword and bare lazy values.

## 日本語

- **F# lazy expression の呼び出しを索引するようになりました** — `lazy compute user` で `compute` を call として記録し、`lazy` キーワードと単独のlazy値は抑止します。
