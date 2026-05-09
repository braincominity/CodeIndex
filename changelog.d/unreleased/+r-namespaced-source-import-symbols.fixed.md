---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R namespace-qualified `source()` calls now surface as import symbols** — forms such as `base::source("R/helpers.R")` are indexed by sourced path.

## 日本語

- **R の namespace 修飾付き `source()` 呼び出しが import シンボルとして出るようになりました** — `base::source("R/helpers.R")` のような形式を source 先パスで索引します。
