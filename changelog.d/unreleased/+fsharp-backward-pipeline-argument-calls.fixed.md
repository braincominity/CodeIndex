---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# backward pipeline argument calls are now indexed** — `printfn <| render user` now exposes `render` as a call reference in addition to the left-hand pipeline callee.

## 日本語

- **F# の backward pipeline 右辺の関数適用も索引するようになりました** — `printfn <| render user` で左辺の呼び出し先に加えて `render` も call reference として取得できます。
