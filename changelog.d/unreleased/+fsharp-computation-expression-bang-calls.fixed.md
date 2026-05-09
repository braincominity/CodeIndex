---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# computation expression bang calls are now indexed** — parenless calls after `do!`, `return!`, and `yield!` now appear as call references.

## 日本語

- **F# computation expression の bang call を索引するようになりました** — `do!`、`return!`、`yield!` の後にある括弧なし呼び出しを call reference として取得できます。
