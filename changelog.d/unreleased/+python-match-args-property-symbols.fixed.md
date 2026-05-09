---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `__match_args__` fields are indexed as properties** — literal names in class-body `__match_args__` assignments now appear as property symbols for symbol search.

## 日本語

- **Python の `__match_args__` field を property として index するようにしました** — class body の `__match_args__` 代入に含まれる literal 名が symbol search 用の property symbol として出るようになりました。
