---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - src/CodeIndex/Cli/IndexCommandRunner.cs
  - src/CodeIndex/Mcp/McpToolHandlers.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python `__all__` re-exports now carry qualified package names in `__init__.py` files** — when a package `__init__.py` lists submodules in `__all__`, cdidx now indexes `package.submodule`-style names in addition to the leaf exports, so exact-name search can find re-exported modules more naturally.

## 日本語

- **Python の `__all__` 再エクスポートは `__init__.py` で修飾済み package 名も持つようになりました** — package の `__init__.py` が `__all__` に submodule を列挙している場合、cdidx は leaf export に加えて `package.submodule` 形式の名前も索引するため、exact-name 検索で再エクスポートされたモジュールを見つけやすくなります。
