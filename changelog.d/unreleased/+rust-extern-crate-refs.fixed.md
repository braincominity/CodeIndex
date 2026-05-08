---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust `extern crate` declarations now emit references** — crate names from `extern crate` items are recorded for reference search without treating aliases as types.

## 日本語

- **Rust の `extern crate` 宣言が reference を出すようになりました** — `extern crate` item の crate 名を参照検索に記録し、alias は型として扱いません。
