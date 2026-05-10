---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc `@return` types now emit type references** — return types documented only in PHPDoc now participate in type reference search.

## 日本語

- **PHPDoc `@return` の型を型参照として索引するようになりました** — PHPDoc にだけ書かれた戻り値型が type reference 検索に反映されます。
