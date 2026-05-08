---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust associated type bounds are now indexed** — declarations such as `type Item: Display + Debug` now expose their bound types as references alongside any default target type.

## 日本語

- **Rust の associated type bounds をインデックスするようになりました** — `type Item: Display + Debug` のような宣言で、default target 型に加えて bound 型も参照として辿れます。
