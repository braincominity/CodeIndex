---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc type alias targets now emit type references** — types mentioned inside `@phpstan-type`, `@psalm-type`, and `@type` alias expressions now participate in reference search.

## 日本語

- **PHPDoc type alias の alias 先を型参照として索引するようになりました** — `@phpstan-type` / `@psalm-type` / `@type` の alias 式に含まれる型が reference 検索に反映されます。
