---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - tests/CodeIndex.Tests/QueryCommandRunnerTests.cs
---

## English

- **Python `__init__.py` direct imports no longer over-qualify dotted import paths** — when a package initializer imports a dotted module path such as `import package.submodule as alias`, cdidx now avoids synthesizing an extra `package.package.submodule`-style exact-name candidate, reducing noisy matches in larger package trees while keeping the useful leaf and alias lookups.

## 日本語

- **Python の `__init__.py` における direct import は dotted な import path を過剰に修飾しなくなりました** — package initializer が `import package.submodule as alias` のように dotted module path を import する場合、cdidx は `package.package.submodule` のような余計な exact-name 候補を生成しないため、大きな package tree でのノイズを減らしつつ leaf / alias の検索性は維持します。
