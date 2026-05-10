---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP static property accesses now emit member references** — `Config::$cache` now indexes `cache` as a reference while preserving the existing class-side type reference.

## 日本語

- **PHP の static property access をメンバー参照として索引するようになりました** — `Config::$cache` で既存の class 側 type reference に加えて `cache` を reference として出します。
