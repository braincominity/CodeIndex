---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python current-package module imports gain package-qualified symbols** — `from .tools import build` inside `__init__.py` now indexes `package.subpkg.tools.build` for exact symbol search.

## 日本語

- **Python の current-package module import に package-qualified symbol を追加しました** — `__init__.py` 内の `from .tools import build` が exact symbol search 用に `package.subpkg.tools.build` を index するようになりました。
