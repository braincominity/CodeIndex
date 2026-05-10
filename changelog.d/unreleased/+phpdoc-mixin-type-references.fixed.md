---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@mixin` types now emit type references** — dynamic mixin declarations now contribute referenced mixin types and generic arguments to search.

## 日本語

- **PHPDoc `@mixin` の型を型参照として索引するようになりました** — dynamic mixin 宣言が、mixin 型と generic 引数の reference を検索へ追加します。
