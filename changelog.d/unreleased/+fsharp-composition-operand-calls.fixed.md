---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# function composition operands are now indexed** — composed functions around `>>` and `<<` are recorded as call-like references.

## 日本語

- **F# の関数合成 operand を索引するようになりました** — `>>` と `<<` の前後にある合成対象の関数を call-like reference として記録します。
