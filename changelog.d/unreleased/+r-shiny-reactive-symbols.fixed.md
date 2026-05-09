---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R Shiny reactive assignments now surface in symbol search** — named `reactive()`, `eventReactive()`, `observe()`, and `observeEvent()` assignments are indexed by their binding name.

## 日本語

- **R Shiny の reactive 代入がシンボル検索に出るようになりました** — `reactive()` / `eventReactive()` / `observe()` / `observeEvent()` の名前付き代入を binding 名で索引します。
