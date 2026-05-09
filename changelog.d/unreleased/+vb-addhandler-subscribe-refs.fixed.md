---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Support/LanguageReferenceExtractionSupport.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **VB `AddHandler` event targets now produce subscribe references** — event names in `AddHandler button.Click, ...` are indexed as `subscribe` edges even when no `Handles` clause is present.

## 日本語

- **VB の `AddHandler` event 対象が subscribe reference になるようにしました** — `Handles` 句がなくても `AddHandler button.Click, ...` の event 名を `subscribe` edge として索引します。
