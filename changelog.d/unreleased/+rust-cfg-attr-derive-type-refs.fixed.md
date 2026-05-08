---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `cfg_attr(..., derive(...))` traits are now indexed** — conditional derives now contribute the same trait type references as direct `derive` attributes.

## 日本語

- **Rust の `cfg_attr(..., derive(...))` trait を index するようになりました** — 条件付き derive でも、直接の `derive` 属性と同じ trait 型参照を記録します。
