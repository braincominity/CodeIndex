---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python functional enum declarations are indexed as classes** — assignments such as `Color = Enum(...)` and `Status = enum.Enum(...)` now appear in symbol search.

## 日本語

- **Python の functional enum 宣言を class として index するようにしました** — `Color = Enum(...)` や `Status = enum.Enum(...)` のような代入が symbol search に出るようになりました。
