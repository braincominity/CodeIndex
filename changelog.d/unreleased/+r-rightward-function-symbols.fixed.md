---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R rightward function assignments now surface in symbol search** — definitions such as `function(x) x + 1 -> increment` and `\(x) x -> name` are indexed by the assigned function name.

## 日本語

- **R の右代入 function 定義がシンボル検索に出るようになりました** — `function(x) x + 1 -> increment` や `\(x) x -> name` のような定義を代入先の関数名で索引します。
