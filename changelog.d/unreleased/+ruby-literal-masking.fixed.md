---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- Ruby block-range scanning now masks `%q`/`%Q` percent literals and heredoc bodies before counting `end`, closing the remaining follow-up edge cases from the previous Ruby range fix.

## 日本語

- Ruby のブロック範囲スキャンで、`end` を数える前に `%q` / `%Q` の percent literal と heredoc 本文をマスクするようにし、前回の Ruby 範囲修正で残っていた follow-up の境界ケースを解消しました。
