---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc template bounds now emit type references** — `@template T of Foo` and `@template U as Bar` constraints now contribute referenced bound types to search.

## 日本語

- **PHPDoc template bound を型参照として索引するようになりました** — `@template T of Foo` / `@template U as Bar` の制約型が reference 検索に反映されます。
