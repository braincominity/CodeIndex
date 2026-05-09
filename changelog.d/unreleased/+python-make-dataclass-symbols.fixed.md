---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `make_dataclass` assignments are indexed as classes** — dynamic dataclass factories such as `DynamicUser = make_dataclass(...)` now appear as class symbols.

## 日本語

- **Python の `make_dataclass` 代入を class として index するようにしました** — `DynamicUser = make_dataclass(...)` のような動的 dataclass factory が class symbol として出るようになりました。
