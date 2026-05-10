---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP catch union types are now indexed** — `catch (\App\Exception\FirstException|SecondException $e)` now emits type references for each exception type.

## 日本語

- **PHP の catch union 型を索引するようになりました** — `catch (\App\Exception\FirstException|SecondException $e)` で各例外型を type reference として出します。
