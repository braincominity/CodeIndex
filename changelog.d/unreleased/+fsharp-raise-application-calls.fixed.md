---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# raise expression calls are now indexed** — `raise buildError user` records `buildError` as a call while suppressing the `raise` keyword and bare exception values.

## 日本語

- **F# raise expression の呼び出しを索引するようになりました** — `raise buildError user` で `buildError` を call として記録し、`raise` キーワードと単独の例外値は抑止します。
