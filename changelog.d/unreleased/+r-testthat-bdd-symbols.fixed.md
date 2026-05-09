---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R testthat BDD blocks now surface in symbol search** — `describe()` and `it()` descriptions are indexed as searchable test symbols, including namespace-qualified calls.

## 日本語

- **R の testthat BDD block がシンボル検索に出るようになりました** — `describe()` と `it()` の description を、namespace 修飾付き呼び出しも含めて検索可能なテストシンボルとして索引します。
