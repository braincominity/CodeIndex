---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/RustReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **Rust attributes now surface as annotation references** — attribute heads such as `#[tokio::test]` and `#[serde(...)]` are now searchable without treating `derive(...)` as a runtime call.

## 日本語

- **Rust attribute を annotation reference として出すようになりました** — `#[tokio::test]` や `#[serde(...)]` のような attribute head を、`derive(...)` を runtime call と誤認せずに検索できます。
