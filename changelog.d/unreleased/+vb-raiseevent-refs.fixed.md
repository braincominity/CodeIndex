---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `RaiseEvent` targets are now indexed** — event names in `RaiseEvent Changed(...)` now emit references so event publishers participate in `references`, `impact`, and graph queries.

## 日本語

- **VB の `RaiseEvent` 対象を索引するようにしました** — `RaiseEvent Changed(...)` の event 名が reference になり、event 発火側も `references`、`impact`、graph query で辿れるようになりました。
