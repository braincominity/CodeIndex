---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `assign()` shorthand function definitions now surface in symbol search** — `assign("compact_plot", \(data) data)` is indexed by the assigned function name.

## 日本語

- **R の `assign()` shorthand function 定義がシンボル検索に出るようになりました** — `assign("compact_plot", \(data) data)` を代入先の関数名で索引します。
