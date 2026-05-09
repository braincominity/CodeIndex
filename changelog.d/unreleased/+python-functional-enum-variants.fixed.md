---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python functional enum variants are indexed as classes** — `IntEnum`, `Flag`, `IntFlag`, and `StrEnum` factory assignments now appear in symbol search.

## 日本語

- **Python の functional enum variant を class として index するようにしました** — `IntEnum`、`Flag`、`IntFlag`、`StrEnum` の factory 代入が symbol search に出るようになりました。
