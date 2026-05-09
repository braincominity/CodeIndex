---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python augmented `__slots__` fields are indexed as properties** — slot names added with `__slots__ += (...)` now appear as property symbols.

## 日本語

- **Python の augmented `__slots__` field を property として index するようにしました** — `__slots__ += (...)` で追加される slot 名も property symbol として出るようになりました。
