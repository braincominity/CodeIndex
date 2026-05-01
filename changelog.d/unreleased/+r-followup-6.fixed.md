---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes named constructor arguments and same-line R6 visibility blocks** — following up on PR #1241, `cdidx` now extracts `methods::setClass`, `methods::setRefClass`, `methods::setClassUnion`, `R6::R6Class`, `methods::setGeneric`, and `methods::setMethod` when their identifying names are written as named arguments, and it also recognizes R6 `public` / `private` / `active` bindings when they appear on the same line as earlier generator arguments, making more common R object-system declarations searchable by symbol.

## 日本語

- **R の検索が named constructor 引数と同一行の R6 visibility ブロックに対応しました** — PR #1241 の follow-up として、`cdidx` は `methods::setClass`、`methods::setRefClass`、`methods::setClassUnion`、`R6::R6Class`、`methods::setGeneric`、`methods::setMethod` を名前付き引数で書いた場合でも抽出できるようになり、さらに R6 の `public` / `private` / `active` binding も先行する generator 引数と同じ行にあれば認識するため、R のオブジェクトシステム宣言をより多くシンボル単位で検索できます。
