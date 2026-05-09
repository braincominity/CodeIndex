---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R named package loader calls now surface as import symbols** — forms such as `library(package = "stringr")` and `require(package = tidyr)` are indexed by package name.

## 日本語

- **R の named package loader call が import シンボルとして出るようになりました** — `library(package = "stringr")` や `require(package = tidyr)` のような形式をパッケージ名で索引します。
