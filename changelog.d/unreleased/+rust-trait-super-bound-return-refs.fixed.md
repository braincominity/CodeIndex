---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust trait superbound function returns are now indexed** — `trait Handler: FnOnce() -> User` keeps `User` visible as a type reference.

## 日本語

- **Rust trait superbound の function 戻り値型が索引されるようになりました** — `trait Handler: FnOnce() -> User` で `User` を type reference として保持します。
