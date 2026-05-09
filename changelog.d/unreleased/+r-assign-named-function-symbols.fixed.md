---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R named-argument `assign()` function definitions now surface in symbol search** — `assign(x = "format_model", value = function(...))` is indexed by the assigned function name.

## 日本語

- **R の named argument 形式の `assign()` 関数定義がシンボル検索に出るようになりました** — `assign(x = "format_model", value = function(...))` を代入先の関数名で索引します。
