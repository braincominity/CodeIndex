---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust trait alias targets now emit type references** — dependencies in `trait Alias = Bound + Other;` declarations are indexed.

## 日本語

- **Rust trait alias の右辺が type reference を出すようになりました** — `trait Alias = Bound + Other;` 宣言内の依存型を索引します。
