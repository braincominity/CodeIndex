---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust enum variant payload types are now indexed** — tuple and struct-style variants such as `Created(User)` and `Moved { from: Point }` now expose their payload types as references attached to the enum.

## 日本語

- **Rust enum variant の payload 型をインデックスするようになりました** — `Created(User)` や `Moved { from: Point }` のような tuple / struct 形式の variant で、payload 型を enum に紐づく参照として辿れます。
