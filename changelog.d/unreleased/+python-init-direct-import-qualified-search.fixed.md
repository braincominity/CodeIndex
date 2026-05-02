---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python `__init__.py` direct imports now index package-qualified module names** — when a package initializer re-exports a sibling module with `import submodule` or `import submodule as alias`, cdidx now adds the `package.submodule` search name alongside the local symbol so exact-name lookup can find the public module path too.

## 日本語

- **Python の `__init__.py` における direct import は package 修飾済みの module 名も索引するようになりました** — package initializer が `import submodule` や `import submodule as alias` で sibling module を再エクスポートしている場合、cdidx は local symbol に加えて `package.submodule` の検索名も追加するため、exact-name 検索で公開された module パスも見つけられます。
