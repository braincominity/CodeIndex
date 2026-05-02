---
category: fixed
affected:
  - src/CodeIndex/Indexer/ReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust raw identifier macro calls are now captured in reference data** — the Rust call extractor recognizes invocations such as `r#type!()` and `crate::r#type!()`, so raw-identifier macro sites are no longer dropped and bare exact searches such as `r#type!` resolve to the canonical stored name.

## 日本語

- **Rust の raw identifier を使った macro 呼び出しが reference data に載るようになりました** — Rust の call extractor が `r#type!()` や `crate::r#type!()` のような呼び出しを認識するため、raw identifier の macro site が落ちなくなり、`r#type!` のような bare な exact search も保存済みの canonical 名に解決されます。
