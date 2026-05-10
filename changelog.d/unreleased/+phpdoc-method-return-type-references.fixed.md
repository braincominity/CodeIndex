---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@method` return types now emit type references** — dynamic method declarations now contribute documented return types and generic arguments to reference search.

## 日本語

- **PHPDoc `@method` の戻り値型を型参照として索引するようになりました** — 動的メソッド宣言が、記録された戻り値型と generic 引数の reference を検索へ追加します。
