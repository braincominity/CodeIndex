---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **R class and R6 constructors now recognize common namespace-qualified forms** — following up on PR #1234, `cdidx` now extracts `methods::setClass`, `methods::setRefClass`, `methods::setClassUnion`, `methods::setGeneric`, `methods::setMethod`, and `R6::R6Class` in addition to the existing unqualified forms, and it also records R6 public/private/active bindings with visibility metadata so more R object-system code is searchable by symbol instead of by file.

## 日本語

- **R の class / R6 コンストラクタがよくある namespace 修飾形式に対応しました** — PR #1234 の follow-up として、`cdidx` は `methods::setClass`、`methods::setRefClass`、`methods::setClassUnion`、`methods::setGeneric`、`methods::setMethod`、`R6::R6Class` を既存の unqualified 形式に加えて抽出し、R6 の public / private / active binding に visibility メタデータも残すため、R のオブジェクトシステムを使うコードをシンボル単位で検索しやすくなります。
