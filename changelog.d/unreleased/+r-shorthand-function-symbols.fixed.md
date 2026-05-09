---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R shorthand function assignments now surface in symbol search** — definitions such as `compact <- \(x) x + 1` are indexed like `function(...)` assignments.

## 日本語

- **R の shorthand function 代入がシンボル検索に出るようになりました** — `compact <- \(x) x + 1` のような定義を `function(...)` 代入と同じく索引します。
