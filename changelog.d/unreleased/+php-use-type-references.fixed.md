---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHP `use` type imports now emit type references** — class imports and trait-use lines now appear in reference results, while `use function` and `use const` stay excluded from type references.

## 日本語

- **PHP の `use` 型 import を型参照として索引するようになりました** — class import と trait-use 行を reference 結果へ出し、`use function` / `use const` は type reference から除外します。
