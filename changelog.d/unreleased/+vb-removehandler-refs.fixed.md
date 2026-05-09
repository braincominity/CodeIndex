---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `RemoveHandler` event targets are now indexed** — event names in `RemoveHandler button.Click, ...` now emit `unsubscribe` references so event unwiring is searchable.

## 日本語

- **VB の `RemoveHandler` event 対象を索引するようにしました** — `RemoveHandler button.Click, ...` の event 名が `unsubscribe` reference になり、event 解除側も検索できます。
