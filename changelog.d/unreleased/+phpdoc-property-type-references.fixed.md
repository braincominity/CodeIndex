---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@property` types now emit type references** — dynamic property declarations now contribute their documented property types and generic arguments to reference search.

## 日本語

- **PHPDoc `@property` の型を型参照として索引するようになりました** — 動的プロパティ宣言が、記録されたプロパティ型と generic 引数の reference を検索へ追加します。
