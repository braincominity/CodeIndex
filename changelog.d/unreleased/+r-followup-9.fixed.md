---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes R6 inheritance metadata** — following up on PR #1261, `cdidx` now extracts the `inherit = ...` class name from `R6::R6Class(...)`, making inherited base classes searchable alongside the existing R object-system constructor coverage.

## 日本語

- **R の検索が R6 の inheritance メタデータに対応しました** — PR #1261 の follow-up として、`cdidx` は `R6::R6Class(...)` の `inherit = ...` から親クラス名も抽出するようになり、既存の R オブジェクトシステム constructor 抽出とあわせて継承元クラスも検索できるようになりました。
