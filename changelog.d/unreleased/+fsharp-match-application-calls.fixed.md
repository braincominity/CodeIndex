---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# match input application calls are now indexed** — `match parse value with` records `parse` as a call while plain `match status with` remains ignored.

## 日本語

- **F# match 入力側の関数適用を索引するようになりました** — `match parse value with` では `parse` を call として記録し、単独値の `match status with` は無視します。
