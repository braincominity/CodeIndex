---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `library(help = ...)` calls now surface as package imports** — help-oriented `library()` / `require()` calls index the referenced package name for symbol search.

## 日本語

- **R の `library(help = ...)` 呼び出しが package import として出るようになりました** — help 表示用の `library()` / `require()` 呼び出しから参照先 package 名を symbol search に索引します。
