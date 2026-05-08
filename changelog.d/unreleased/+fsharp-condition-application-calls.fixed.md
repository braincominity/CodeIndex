---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# condition application calls are now indexed** — parenless calls at the start of `if`, `elif`, and `while` conditions now appear as call references.

## 日本語

- **F# 条件式先頭の関数適用を索引するようになりました** — `if`、`elif`、`while` 条件の先頭にある括弧なし呼び出しを call reference として取得できます。
