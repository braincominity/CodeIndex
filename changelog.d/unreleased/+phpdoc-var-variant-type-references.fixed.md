---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/PhpReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **PHPDoc analyzer var tags now emit type references** — `@phpstan-var` and `@psalm-var` are now indexed like regular `@var` tags.

## 日本語

- **PHPDoc analyzer var tag を型参照として索引するようになりました** — `@phpstan-var` / `@psalm-var` を通常の `@var` と同様に扱います。
