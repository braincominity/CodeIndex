---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust generic default types are now indexed** — default type arguments such as `T = User` in generic parameter lists now emit type references.

## 日本語

- **Rust generic default 型を index するようになりました** — generic parameter list の `T = User` のような default 型を type reference として記録します。
