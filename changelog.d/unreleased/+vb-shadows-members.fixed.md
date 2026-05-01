---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB.NET `Shadows` members are indexed again** — `Shadows` now counts as a VB member modifier, so `Shadows Sub`, `Shadows Function`, and `Shadows Property` declarations stay searchable instead of being skipped.

## 日本語

- **VB.NET の `Shadows` 付き member を再び索引化するようにしました** — `Shadows` を VB の member modifier として扱うため、`Shadows Sub` / `Shadows Function` / `Shadows Property` の宣言が検索漏れしなくなります。
