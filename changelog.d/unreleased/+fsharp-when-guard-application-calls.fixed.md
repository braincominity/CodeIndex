---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# pattern guard calls are now indexed** — `when validate user ->` records `validate` as a call while bare guard values remain ignored.

## 日本語

- **F# pattern guard の関数呼び出しを索引するようになりました** — `when validate user ->` では `validate` を call として記録し、単独のguard値は無視します。
