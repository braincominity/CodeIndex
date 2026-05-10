---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP parameter class types are now indexed** — typed parameters such as `Request $request` and `?User $user` now emit type references while builtin scalar types remain ignored.

## 日本語

- **PHP の引数 class 型を索引するようになりました** — `Request $request` や `?User $user` のような型付き引数を type reference として出し、builtin scalar 型は無視します。
