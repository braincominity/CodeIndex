---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes `methods::setAs` class aliases** — following up on PR #1269, `cdidx` now extracts both sides of `methods::setAs(from = ..., to = ...)` as searchable class symbols, extending the existing R object-system constructor coverage.

## 日本語

- **R の検索が `methods::setAs` の class alias に対応しました** — PR #1269 の follow-up として、`cdidx` は `methods::setAs(from = ..., to = ...)` の両側を検索可能な class シンボルとして抽出するようになり、既存の R オブジェクトシステム constructor 抽出がさらに広がりました。
