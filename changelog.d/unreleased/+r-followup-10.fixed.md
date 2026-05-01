---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes R6 inheritance vectors** — following up on PR #1269, `cdidx` now extracts the first base-class name from `R6::R6Class(..., inherit = c(...))` as a searchable class symbol, extending the existing R object-system constructor coverage.

## 日本語

- **R の検索が R6 の inheritance vector に対応しました** — PR #1269 の follow-up として、`cdidx` は `R6::R6Class(..., inherit = c(...))` の先頭の親クラス名を検索可能な class シンボルとして抽出するようになり、既存の R オブジェクトシステム constructor 抽出がさらに広がりました。
