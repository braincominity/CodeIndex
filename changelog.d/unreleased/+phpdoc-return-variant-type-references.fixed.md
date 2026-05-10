---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc analyzer return tags now emit type references** — `@phpstan-return` and `@psalm-return` are now indexed like regular `@return` tags.

## 日本語

- **PHPDoc analyzer return tag を型参照として索引するようになりました** — `@phpstan-return` / `@psalm-return` を通常の `@return` と同様に扱います。
