---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc imported type sources now emit type references** — `@phpstan-import-type` and `@psalm-import-type` `from` classes now appear in type reference search.

## 日本語

- **PHPDoc import-type の参照元を型参照として索引するようになりました** — `@phpstan-import-type` / `@psalm-import-type` の `from` class が type reference 検索に出るようになります。
