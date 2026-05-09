---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `assign()` function definitions now surface in symbol search** — clear definitions such as `assign("build_plot", function(...))` are indexed by the assigned function name.

## 日本語

- **R の `assign()` 関数定義がシンボル検索に出るようになりました** — `assign("build_plot", function(...))` のような明確な定義を、代入先の関数名で索引します。
