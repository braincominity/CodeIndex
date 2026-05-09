---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `pacman::p_load()` calls now surface as package imports** — multiple bare or quoted package arguments are indexed as import symbols.

## 日本語

- **R の `pacman::p_load()` 呼び出しが package import として出るようになりました** — 裸名または引用付きの複数 package 引数を import symbol として索引します。
