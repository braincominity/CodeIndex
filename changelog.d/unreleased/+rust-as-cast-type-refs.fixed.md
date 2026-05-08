---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `as` casts now index target types** — casts such as `value as User` and pointer casts record the real target type references for search and impact queries.

## 日本語

- **Rust の `as` cast が変換先型を index するようになりました** — `value as User` や pointer cast で、検索・影響調査に使える実型参照を記録します。
