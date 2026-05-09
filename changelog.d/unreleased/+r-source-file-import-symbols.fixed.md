---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `source()` file loads now surface as import symbols** — explicit file loads such as `source("R/helpers.R")` are indexed by the sourced path.

## 日本語

- **R の `source()` ファイル読み込みが import シンボルとして出るようになりました** — `source("R/helpers.R")` のような明示的な読み込みを source 先パスで索引します。
