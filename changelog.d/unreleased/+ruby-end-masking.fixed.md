---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- Ruby `def`/`class` range detection now ignores `end` tokens inside strings and comments, so `definition`, `outline`, and related navigation stay accurate for common Ruby source patterns.

## 日本語

- Ruby の `def` / `class` の範囲判定で、文字列やコメント内の `end` を無視するようにしました。これにより、よくある Ruby ソースのパターンでも `definition` / `outline` などのナビゲーション精度が保たれます。
