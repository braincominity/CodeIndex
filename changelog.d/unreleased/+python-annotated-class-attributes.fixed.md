---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python annotated class attributes are indexed as properties** — class-body declarations such as `name: str` now appear in symbol search while method-local annotations stay excluded.

## 日本語

- **Python の型注釈付き class attribute を property として index するようにしました** — `name: str` のような class body 宣言が symbol search に出るようになり、method-local annotation は除外されたままです。
