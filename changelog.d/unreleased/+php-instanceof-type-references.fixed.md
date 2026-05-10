---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP `instanceof` operands are now indexed as type references** — expressions such as `$value instanceof \App\Domain\UserService` now emit both qualified and short type-reference entries.

## 日本語

- **PHP の `instanceof` 右辺を型参照として索引するようになりました** — `$value instanceof \App\Domain\UserService` のような式で、完全修飾名と短い型名の両方を type reference として出します。
