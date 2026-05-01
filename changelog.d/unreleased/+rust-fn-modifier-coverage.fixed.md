---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust function symbol extraction now accepts broader modifier combinations** — `fn` declarations with `default` and `extern` (with or without ABI literals such as `"C-unwind"`) are now recognized, improving Rust symbol search coverage.

## 日本語

- **Rust 関数シンボル抽出で修飾子の組み合わせ対応を拡張しました** — `default` や `extern`（`"C-unwind"` のような ABI 文字列あり/なし）を含む `fn` 宣言を認識できるようになり、Rust のシンボル検索カバレッジが向上しました。
