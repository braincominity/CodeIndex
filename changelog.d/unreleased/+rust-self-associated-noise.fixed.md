---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `Self` associated calls no longer create self type edges** — `Self::new()` and `Self { ... }` stop emitting `Self` as an external type reference or instantiation while concrete receivers remain indexed.

## 日本語

- **Rust の `Self` associated call が自己 type edge を出さなくなりました** — `Self::new()` や `Self { ... }` は `Self` を外部 type reference / instantiate として出さず、具体型 receiver は従来通り索引します。
