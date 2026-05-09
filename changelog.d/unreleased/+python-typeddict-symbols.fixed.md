---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python functional `TypedDict` declarations are indexed as classes** — assignments such as `UserPayload = TypedDict(...)` and `OrderPayload = typing.TypedDict(...)` now appear in symbol search.

## 日本語

- **Python の functional `TypedDict` 宣言を class として index するようにしました** — `UserPayload = TypedDict(...)` や `OrderPayload = typing.TypedDict(...)` のような代入が symbol search に出るようになりました。
