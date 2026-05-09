---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R namespace-qualified package loader calls now surface as import symbols** — forms such as `base::library("readr")` and `base::requireNamespace(package = "rlang")` are indexed by package name.

## 日本語

- **R の namespace 修飾付き package loader call が import シンボルとして出るようになりました** — `base::library("readr")` や `base::requireNamespace(package = "rlang")` のような形式をパッケージ名で索引します。
