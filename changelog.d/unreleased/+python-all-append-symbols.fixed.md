---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `__all__.append` exports are indexed** — package `__init__.py` files that append literal names to `__all__` now expose those names as import symbols for exact symbol search.

## 日本語

- **Python の `__all__.append` export を index するようにしました** — package の `__init__.py` が literal 名を `__all__` に append する場合も、exact symbol search 用の import symbol として公開名を index するようになりました。
