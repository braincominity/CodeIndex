---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `__slots__` fields are indexed as properties** — literal slot names in class-body `__slots__` assignments now appear as property symbols instead of only indexing `__slots__` itself.

## 日本語

- **Python の `__slots__` field を property として index するようにしました** — class body の `__slots__` 代入に含まれる literal slot 名を property symbol として出し、`__slots__` 自体だけが index される状態を避けるようにしました。
