---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R S4 and reference class definitions now surface in symbol search** — `cdidx` now extracts R `setClass` and `setRefClass` declarations as classes, plus `setGeneric` and `setMethod` declarations as functions, so R projects that lean on S4-style object systems get more useful symbol search results instead of falling back to file-level matches.

## 日本語

- **R の S4 / reference class 定義がシンボル検索に出るようになりました** — `cdidx` は R の `setClass` と `setRefClass` を class として、`setGeneric` と `setMethod` を function として抽出するため、S4 系のオブジェクトシステムを使う R プロジェクトでファイル単位の一致に落ちにくくなります。
