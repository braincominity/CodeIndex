---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python assigned class attributes are indexed as properties** — class-body assignments such as `DEFAULT_TIMEOUT = 30` now appear in symbol search while method-local assignments stay excluded.

## 日本語

- **Python の代入形式 class attribute を property として index するようにしました** — `DEFAULT_TIMEOUT = 30` のような class body の代入が symbol search に出るようになり、method-local assignment は除外されたままです。
