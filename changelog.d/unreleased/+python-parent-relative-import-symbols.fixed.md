---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python parent-relative imports gain package-qualified symbols** — `from ..shared import helper` inside package `__init__.py` now indexes the resolved package path, such as `package.shared.helper`, for exact symbol search.

## 日本語

- **Python の parent-relative import に package-qualified symbol を追加しました** — package の `__init__.py` 内の `from ..shared import helper` が、`package.shared.helper` のような解決後の package path を exact symbol search 用に index するようになりました。
