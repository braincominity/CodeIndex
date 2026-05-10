---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc parameter tag variants now emit type references** — `@phpstan-param`, `@psalm-param`, and `@param-out` are now indexed like regular `@param` tags.

## 日本語

- **PHPDoc parameter tag variant を型参照として索引するようになりました** — `@phpstan-param` / `@psalm-param` / `@param-out` を通常の `@param` と同様に扱います。
