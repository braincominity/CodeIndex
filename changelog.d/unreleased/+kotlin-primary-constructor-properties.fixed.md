---
category: fixed
issues:
  - 1127
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Kotlin primary constructor properties are now extracted** — `data class` and `enum class` constructor parameters declared with `val`/`var` are now emitted as `property` symbols, including visibility and multi-line constructor headers.

## 日本語

- **Kotlin の primary constructor プロパティを抽出するようになりました** — `data class` と `enum class` の `val`/`var` 付きコンストラクタ引数が `property` シンボルとして出力され、可視性と複数行の constructor ヘッダにも対応します。
