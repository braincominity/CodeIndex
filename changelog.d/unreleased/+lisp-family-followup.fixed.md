---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Racket and Common Lisp follow-up search coverage now surface additional top-level forms** — following up on PR #1175, `cdidx` now extracts Racket `module`, `define`, `struct`, and `require` symbols and also recognizes Common Lisp `in-package` package contexts, so Lisp-family projects no longer fall back to file-only results as often.

## 日本語

- **Racket と Common Lisp の follow-up 検索カバレッジが追加のトップレベル構文を拾うようになりました** — PR #1175 の follow-up として、`cdidx` は Racket の `module`、`define`、`struct`、`require` を抽出し、Common Lisp の `in-package` パッケージ文脈も認識するため、Lisp 系プロジェクトでファイル単位の結果に落ちるケースがさらに減ります。
