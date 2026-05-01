---
category: fixed
affected:
  - src/CodeIndex/Database/DbReader.cs
  - tests/CodeIndex.Tests/DbReaderTests.cs
---

## English

- **Rust macro queries now accept the trailing `!` used in source code** — symbol search and definition lookup normalize `my_macro!` to the stored macro name `my_macro`, so pasting Rust macro invocations into `cdidx` now finds the expected result.

## 日本語

- **Rust のマクロ検索でソース上の末尾 `!` をそのまま使えるようになりました** — シンボル検索と定義検索で `my_macro!` を保存済みのマクロ名 `my_macro` に正規化するため、Rust のマクロ呼び出しをそのまま貼り付けても期待どおりの結果が返ります。
