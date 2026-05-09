---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# block-boundary values no longer look like calls** — bare values before `do` or `else`, such as `for item in items do` and `then readyValue else`, are no longer indexed as call references.

## 日本語

- **F# block 境界の単独値を call と誤認しなくなりました** — `for item in items do` や `then readyValue else` のように `do` / `else` の前にある単独値を call 参照として索引しません。
