---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust tuple-style constructors now emit instantiation references** — `User(...)`, `Some(...)`, and `Ok(...)` are classified as construction edges instead of ordinary calls.

## 日本語

- **Rust の tuple-style constructor が instantiate reference を出すようになりました** — `User(...)`、`Some(...)`、`Ok(...)` を通常 call ではなく構築エッジとして分類します。
