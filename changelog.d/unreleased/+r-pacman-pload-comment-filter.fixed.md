---
category: fixed
affected:
  - src/CodeIndex/Indexer/Symbols/SymbolExtractor.cs
  - tests/CodeIndex.Tests/SymbolExtractorTests.cs
---

## English

- **R `pacman::p_load()` import extraction now ignores trailing comments** — package-like text after `#` is no longer indexed as an import.

## 日本語

- **R の `pacman::p_load()` import 抽出が末尾コメントを無視するようになりました** — `#` 以降の package 風テキストを import として索引しません。
