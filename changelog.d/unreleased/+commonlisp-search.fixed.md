---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
  - README.md
---

## English

- **Common Lisp files now surface top-level definitions in search** — `cdidx` now extracts `defpackage`, `defclass`, `defstruct`, `defparameter`/`defvar`/`defconstant`, `defun`, `defmacro`, `defgeneric`, and `defmethod` symbols from `.lisp`, `.lsp`, and `.cl` files so Lisp projects produce useful symbol search results instead of falling back to file-only matches.

## 日本語

- **Common Lisp ファイルでトップレベル定義が検索に出るようになりました** — `cdidx` は `.lisp` / `.lsp` / `.cl` ファイルから `defpackage`、`defclass`、`defstruct`、`defparameter` / `defvar` / `defconstant`、`defun`、`defmacro`、`defgeneric`、`defmethod` を抽出するため、Lisp プロジェクトでシンボル検索が効くようになり、ファイル単位の一致だけに落ちなくなりました。
