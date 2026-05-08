---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Ruby operator method definitions are now indexed as functions** — `cdidx` records definitions such as `def []`, `def []=`, and `def <=>`, improving symbol navigation for Ruby value objects and collection APIs.

## 日本語

- **Ruby の operator method 定義を関数として索引するようになりました** — `cdidx` は `def []`、`def []=`、`def <=>` のような定義を記録し、Ruby の値オブジェクトや collection API のシンボル移動を改善します。
