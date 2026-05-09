---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/FSharpReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
---

## English

- **F# chained function composition now indexes every operand** — expressions such as `validate >> normalize >> persist` record all composed functions.

## 日本語

- **F# の連鎖した関数合成で全operandを索引するようになりました** — `validate >> normalize >> persist` のような式で、合成対象の全関数を記録します。
