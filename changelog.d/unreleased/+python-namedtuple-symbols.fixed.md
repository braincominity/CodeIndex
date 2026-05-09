---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python functional named tuples are indexed as classes** — assignments such as `Point = NamedTuple(...)` and `Coordinate = collections.namedtuple(...)` now appear as class symbols.

## 日本語

- **Python の functional named tuple を class として index するようにしました** — `Point = NamedTuple(...)` や `Coordinate = collections.namedtuple(...)` のような代入が class symbol として出るようになりました。
