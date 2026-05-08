---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust struct literals now emit instantiation references** — `User { ... }` and path-qualified struct literals are visible to reference and impact queries.

## 日本語

- **Rust の struct literal が instantiate reference を出すようになりました** — `User { ... }` や path-qualified struct literal を参照・影響調査で見つけられます。
