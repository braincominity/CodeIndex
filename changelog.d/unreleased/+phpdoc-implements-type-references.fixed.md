---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@implements` types now emit type references** — generic interface declarations, including PHPStan and Psalm variants, now contribute interface and type-argument references.

## 日本語

- **PHPDoc `@implements` の型を型参照として索引するようになりました** — PHPStan / Psalm 形式を含む generic interface 宣言が、interface 型と型引数の reference を追加します。
