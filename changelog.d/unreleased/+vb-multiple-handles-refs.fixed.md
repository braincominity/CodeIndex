---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `Handles` clauses now index every event target** — comma-separated targets such as `Handles button.Click, menu.Opened` now produce one `subscribe` reference per event without confusing parameter-list commas for events.

## 日本語

- **VB の `Handles` 句で全 event 対象を索引するようにしました** — `Handles button.Click, menu.Opened` のような comma 区切り対象が event ごとに `subscribe` reference になり、引数リストの comma は event と誤認しません。
