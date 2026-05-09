---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python class `__annotations__` dictionary keys are indexed as properties** — literal keys in class-body `__annotations__` assignments now appear in symbol search.

## 日本語

- **Python class の `__annotations__` 辞書 key を property として index するようにしました** — class body の `__annotations__` 代入に含まれる literal key が symbol search に出るようになりました。
