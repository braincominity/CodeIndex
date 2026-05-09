---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# assertion predicate calls are now indexed** — `assert validate user` records `validate` as a call while suppressing the `assert` keyword itself.

## 日本語

- **F# assertion の述語呼び出しを索引するようになりました** — `assert validate user` で `validate` を call として記録し、`assert` キーワード自体は抑止します。
