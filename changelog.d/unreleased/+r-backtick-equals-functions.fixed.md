---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R backtick-named `=` function assignments now surface in symbol search** — definitions such as `` `plot-model` = function(...) `` are indexed like their `<-` equivalents.

## 日本語

- **R のバッククォート名付き `=` 関数代入がシンボル検索に出るようになりました** — `` `plot-model` = function(...) `` のような定義を `<-` 形式と同じく索引します。
