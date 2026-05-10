---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@method` parameter types now emit type references** — dynamic method parameter annotations now contribute non-builtin parameter types to reference search.

## 日本語

- **PHPDoc `@method` の引数型を型参照として索引するようになりました** — 動的メソッドの parameter annotation が、組み込み型以外の引数型 reference を検索へ追加します。
