---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search coverage now includes more S4, union, and R6 forms** — following up on PR #1215, `cdidx` now extracts unquoted `setMethod` declarations, recognizes additional class-definition forms such as `setClassUnion` and `R6Class`, and keeps the existing `setClass`, `setRefClass`, `setGeneric`, and function-assignment coverage so R projects using richer object systems surface more useful symbol search results.

## 日本語

- **R の検索カバレッジが S4 / union / R6 まで広がりました** — PR #1215 の follow-up として、`cdidx` はクォートなしの `setMethod` 宣言を抽出し、`setClassUnion` や `R6Class` のような追加の class 定義形式も認識するため、より豊かなオブジェクトシステムを使う R プロジェクトで検索結果が見つかりやすくなります。
