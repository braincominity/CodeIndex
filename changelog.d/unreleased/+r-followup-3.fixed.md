---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R6 public methods now surface in symbol search** — following up on PR #1222, `cdidx` now extracts R6-style `name = function(...)` public method definitions in addition to the existing `setClass`, `setRefClass`, `setClassUnion`, `R6Class`, `setGeneric`, and `setMethod` coverage, so R6 projects expose their method names instead of falling back to file-only search results.

## 日本語

- **R6 の public メソッドがシンボル検索に出るようになりました** — PR #1222 の follow-up として、`cdidx` は R6 でよく使う `name = function(...)` 形式の public メソッド定義を抽出し、既存の `setClass`、`setRefClass`、`setClassUnion`、`R6Class`、`setGeneric`、`setMethod` とあわせて、R6 プロジェクトでメソッド名から検索しやすくします。
