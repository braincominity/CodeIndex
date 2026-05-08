---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust function-trait bound return types are now indexed** — `F: FnOnce() -> Result<User, Error>` and matching `where` clauses keep the `Result`/payload return dependencies visible.

## 日本語

- **Rust function-trait bound の戻り値型が索引されるようになりました** — `F: FnOnce() -> Result<User, Error>` や同等の `where` 句で `Result` と payload の戻り値依存を保持します。
