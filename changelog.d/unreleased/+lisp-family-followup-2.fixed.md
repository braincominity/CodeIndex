---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Racket and Common Lisp follow-up coverage now includes more module and package forms** — following up on PR #1185, `cdidx` now extracts Racket `provide`, `define-syntax-rule`, and `define-for-syntax` symbols and also recognizes Common Lisp `use-package` in addition to `in-package`, so Lisp-family projects get more useful symbol search results from common package and macro patterns.

## 日本語

- **Racket と Common Lisp の follow-up カバレッジがさらに module / package 構文を拾うようになりました** — PR #1185 の follow-up として、`cdidx` は Racket の `provide`、`define-syntax-rule`、`define-for-syntax` を抽出し、Common Lisp では `in-package` に加えて `use-package` も認識するため、Lisp 系プロジェクトでよく使う package / macro パターンから検索結果を得やすくなります。
