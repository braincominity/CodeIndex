---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `NewType` aliases are indexed** — aliases such as `UserId = NewType("UserId", int)` and `OrderId = typing.NewType(...)` now appear in symbol search.

## 日本語

- **Python の `NewType` alias を index するようにしました** — `UserId = NewType("UserId", int)` や `OrderId = typing.NewType(...)` のような alias が symbol search に出るようになりました。
