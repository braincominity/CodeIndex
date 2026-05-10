---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc analyzer property tags now emit type references** — `@phpstan-property*` and `@psalm-property*` types are now indexed like regular `@property` tags.

## 日本語

- **PHPDoc analyzer property tag の型を型参照として索引するようになりました** — `@phpstan-property*` / `@psalm-property*` の型を通常の `@property` と同様に扱います。
