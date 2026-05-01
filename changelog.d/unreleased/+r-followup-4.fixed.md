---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R class definitions now handle bare names and more R6 bindings** — following up on PR #1226, `cdidx` now recognizes unquoted `setClass`, `setRefClass`, `setClassUnion`, and `R6Class` names, and it continues surfacing R6 public, private, and active `name = function(...)` bindings as function symbols, so more R object-system code is searchable by symbol instead of by file.

## 日本語

- **R の class 定義が裸の名前とより多くの R6 binding に対応しました** — PR #1226 の follow-up として、`cdidx` はクォートなしの `setClass`、`setRefClass`、`setClassUnion`、`R6Class` を認識し、R6 の public / private / active な `name = function(...)` binding も function シンボルとして出すため、R のオブジェクトシステムを使うコードをシンボル単位で検索しやすくなります。
