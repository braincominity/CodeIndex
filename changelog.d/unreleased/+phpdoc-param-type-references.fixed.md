---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@param` types now emit type references** — docblock-only parameter types such as `@param \App\Models\User|Guest $actor` now participate in type reference search.

## 日本語

- **PHPDoc `@param` の型を型参照として索引するようになりました** — `@param \App\Models\User|Guest $actor` のような docblock 専用の引数型が type reference 検索に反映されます。
