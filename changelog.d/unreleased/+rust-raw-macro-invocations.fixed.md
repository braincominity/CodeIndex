---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust raw identifier macro calls are now searchable** — the Rust call extractor recognizes invocations such as `r#type!()` and `crate::r#type!()`, so reference search and graph navigation keep matching the canonical stored name instead of dropping raw-identifier macro sites.

## 日本語

- **Rust の raw identifier を使った macro 呼び出しも検索できるようになりました** — Rust の call extractor が `r#type!()` や `crate::r#type!()` のような呼び出しを認識するため、reference search や graph navigation が raw identifier の macro site を落とさず、保存済みの canonical 名に一致し続けます。
