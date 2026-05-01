---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R search now recognizes named constructor arguments** — following up on PR #1254, `cdidx` now extracts `methods::setClass`, `methods::setRefClass`, `methods::setClassUnion`, `methods::setOldClass`, `methods::setValidity`, `R6::R6Class`, `methods::setGeneric`, and `methods::setMethod` when their identifying names are written as named arguments, in addition to the existing R6 `public` / `private` / `active` method coverage, making more common R object-system declarations searchable by symbol.

## 日本語

- **R の検索が named constructor 引数に対応しました** — PR #1254 の follow-up として、`cdidx` は `methods::setClass`、`methods::setRefClass`、`methods::setClassUnion`、`methods::setOldClass`、`methods::setValidity`、`R6::R6Class`、`methods::setGeneric`、`methods::setMethod` を名前付き引数で書いた場合でも抽出できるようになり、既存の R6 `public` / `private` / `active` method 抽出とあわせて、R のオブジェクトシステム宣言をより多くシンボル単位で検索できます。
