---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# backward pipeline callees are now indexed** — calls such as `printfn <| value` and `<||` variants now expose the left-hand function as a call reference.

## 日本語

- **F# の backward pipeline の呼び出し先を索引するようになりました** — `printfn <| value` や `<||` 系の左辺関数を call reference として取得できます。
