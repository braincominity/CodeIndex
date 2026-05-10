---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP return class types are now indexed** — return declarations such as `(): Response|JsonResponse` now emit type references for non-builtin result types.

## 日本語

- **PHP の戻り値 class 型を索引するようになりました** — `(): Response|JsonResponse` のような戻り値宣言で、builtin ではない結果型を type reference として出します。
