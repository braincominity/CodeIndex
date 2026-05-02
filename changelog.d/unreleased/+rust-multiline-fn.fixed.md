---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust multiline `fn` headers now keep their symbol positions** — wrapped function signatures are collected as a single statement before matching, so `pub unsafe extern` headers and similar multiline forms still index the function name on the correct line and column.

## 日本語

- **Rust の複数行 `fn` ヘッダが symbol 位置を保持するようになりました** — 折り返された関数シグネチャを 1 つの statement としてまとめてから照合するため、`pub unsafe extern` のような複数行形式でも関数名を正しい行・列で索引できるようになりました。
