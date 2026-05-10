---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP property class types are now indexed** — property declarations such as `private User|Guest $owner` now emit type references for non-builtin property types.

## 日本語

- **PHP のプロパティ class 型を索引するようになりました** — `private User|Guest $owner` のようなプロパティ宣言で、builtin ではないプロパティ型を type reference として出します。
