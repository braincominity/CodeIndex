---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes `methods::setIs` class relationships** — following up on PR #1277, `cdidx` now extracts the target class from `methods::setIs(class1 = ..., class2 = ...)` as a searchable class symbol, extending the existing R object-system constructor coverage.

## 日本語

- **R の検索が `methods::setIs` の class 関係に対応しました** — PR #1277 の follow-up として、`cdidx` は `methods::setIs(class1 = ..., class2 = ...)` の target class を検索可能な class シンボルとして抽出するようになり、既存の R オブジェクトシステム constructor 抽出がさらに広がりました。
