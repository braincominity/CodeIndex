---
category: fixed
affected:
  - src/CodeIndex/Indexer/References/Languages/SqlReferenceExtractor.cs
  - tests/CodeIndex.Tests/ReferenceExtractorTests.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **T-SQL trigger enable/disable statements now index their table targets** — exact SQL reference search can find tables touched only by `ENABLE TRIGGER ... ON` or `DISABLE TRIGGER ... ON` maintenance scripts.

## 日本語

- **T-SQL の trigger enable/disable 文で table target を索引するようになりました** — `ENABLE TRIGGER ... ON` / `DISABLE TRIGGER ... ON` の maintenance script だけで触られる table も、SQL の exact reference search で見つけられるようになりました。
