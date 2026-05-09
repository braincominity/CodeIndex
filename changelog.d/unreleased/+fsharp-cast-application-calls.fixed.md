---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# cast expression calls are now indexed by the inner function** — `upcast createWidget user` records `createWidget` as a call while suppressing the `upcast` and `downcast` keywords.

## 日本語

- **F# cast expression の呼び出しを内側の関数名で索引するようになりました** — `upcast createWidget user` で `createWidget` を call として記録し、`upcast` / `downcast` キーワードは抑止します。
