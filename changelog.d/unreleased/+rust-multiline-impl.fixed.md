---
category: fixed
affected:
  - src/CodeIndex/Indexer/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **Rust multiline `impl` headers now keep their symbol positions** — wrapped `unsafe impl` / `impl ... for ...` blocks are now collected as a single statement before matching, so the implementing type is indexed even when the header spans several lines.

## 日本語

- **Rust の複数行 `impl` ヘッダが symbol 位置を保持するようになりました** — 折り返された `unsafe impl` / `impl ... for ...` ブロックを 1 つの statement としてまとめてから照合するため、ヘッダが複数行にまたがっても実装対象型を索引できるようになりました。
