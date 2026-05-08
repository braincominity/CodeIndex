---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# condition extraction avoids bare boolean values** — `if isReady then ...` no longer records `isReady` as a call while `if validate user then ...` remains indexed.

## 日本語

- **F# 条件抽出で単独の boolean 値を call と誤認しないようになりました** — `if isReady then ...` では `isReady` を call として記録せず、`if validate user then ...` は引き続き索引します。
