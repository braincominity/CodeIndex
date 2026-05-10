---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@extends` types now emit type references** — generic parent declarations, including `@phpstan-extends` and `@psalm-extends`, now contribute parent and type-argument references.

## 日本語

- **PHPDoc `@extends` の型を型参照として索引するようになりました** — `@phpstan-extends` / `@psalm-extends` を含む generic parent 宣言が、親型と型引数の reference を追加します。
