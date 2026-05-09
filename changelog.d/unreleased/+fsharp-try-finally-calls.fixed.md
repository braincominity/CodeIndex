---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# `try`/`finally` application calls are now indexed** — parenless calls after `try` and `finally` now appear in call-reference results.

## 日本語

- **F# の `try` / `finally` 後の関数適用を索引するようになりました** — `try` や `finally` の直後にある括弧なし呼び出しを call reference として取得できます。
