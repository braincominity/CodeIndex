---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.Python.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Python current-package re-exports gain qualified import symbols** — `from . import helper` inside `__init__.py` now indexes both `helper` and the package-qualified module name for exact symbol search.

## 日本語

- **Python の current-package re-export に修飾済み import symbol を追加しました** — `__init__.py` 内の `from . import helper` が `helper` と package-qualified module name の両方を index するようになり、exact symbol search で見つけやすくなりました。
