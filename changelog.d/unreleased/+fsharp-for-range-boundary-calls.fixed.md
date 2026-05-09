---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/ReferenceExtractor.cs
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# `for` range boundary calls are now indexed** — `to endIndex user` and `downto lowerBound user` record their boundary functions while suppressing the range keywords.

## 日本語

- **F# `for` range 境界の呼び出しを索引するようになりました** — `to endIndex user` や `downto lowerBound user` の境界関数を記録し、rangeキーワード自体は抑止します。
