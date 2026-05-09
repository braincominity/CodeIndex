---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python `__all__.extend` exports are indexed** — package `__init__.py` files that extend `__all__` with literal names now expose those names as import symbols for exact symbol search.

## 日本語

- **Python の `__all__.extend` export を index するようにしました** — package の `__init__.py` が literal 名で `__all__` を extend する場合も、exact symbol search 用の import symbol として公開名を index するようになりました。
