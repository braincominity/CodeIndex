---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **VB.NET `Overloads` members are indexed again** — `Overloads` now counts as a VB member modifier, so `Overloads Sub`, `Overloads Function`, and `Overloads Property` declarations stay searchable instead of being skipped.

## 日本語

- **VB.NET の `Overloads` 付き member を再び索引化するようにしました** — `Overloads` を VB の member modifier として扱うため、`Overloads Sub` / `Overloads Function` / `Overloads Property` の宣言が検索漏れしなくなります。
